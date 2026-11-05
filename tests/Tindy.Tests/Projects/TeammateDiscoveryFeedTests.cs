using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class TeammateDiscoveryFeedTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Fact]
    public async Task MoreThanThousandCandidates_AllRanked_BatchedQueries_FrozenScores_LiveEligibility_AndNoRepeats()
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var seed = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var skill = Guid.NewGuid(); var name = skill.ToString();
        seed.Skills.Add(new() { SkillId = skill, Name = name, NameNormalized = name });
        var users = Enumerable.Range(0, 1005).Select(_ => RecruitmentData.User()).ToArray();
        foreach (var user in users) user.Profile = TeammateTestData.Profile(user.UserId, data.Role, skill);
        // The best candidate is deliberately beyond the first database page.
        var sorted = users.OrderBy(u => u.UserId).ToArray();
        foreach (var user in users) user.Profile!.AvailableHoursPerWeek = 1;
        sorted[^1].Profile!.AvailableHoursPerWeek = 10;
        seed.Users.AddRange(users); await seed.SaveChangesAsync();
        var counter = new QueryCounter();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(seed.Database.GetConnectionString()).AddInterceptors(counter).Options);
        var clock = new ProjectTestClock(DateTime.UtcNow);
        var sessions = new TeammateDiscoveryFeedSessions(clock);
        var service = new TeammateDiscoveryService(new TeammateDiscoveryRepository(db), sessions, clock);
        var filters = new TeammateDiscoveryFilters(SkillId: skill);
        var page = await service.GetAsync(data.Leader, data.Project, data.Position, 20, null, filters);
        Assert.InRange(counter.Count, 1, 8);
        Assert.Equal(sorted[^1].UserId, page.Items[0].Candidate.UserId);
        var firstCursor = page.NextCursor!;
        var removed = page.Items[^1].Candidate.UserId;
        var unseen = sorted.First(u => !page.Items.Any(c => c.Candidate.UserId == u.UserId));
        unseen.Profile!.OpenToTeamInvitations = false;
        // Fresh profile must not rewrite the snapshot preview/score of another candidate.
        var frozen = sorted.Last(u => !page.Items.Any(c => c.Candidate.UserId == u.UserId) && u.UserId != unseen.UserId);
        frozen.Profile!.AvailableHoursPerWeek = 40;
        await seed.SaveChangesAsync();
        var seen = page.Items.Select(c => c.Candidate.UserId).ToList();
        while (page.HasMore)
        {
            counter.Count = 0;
            page = await service.GetAsync(data.Leader, data.Project, data.Position, 20, page.NextCursor, filters);
            Assert.InRange(counter.Count, 1, 8);
            if (page.Items.SingleOrDefault(c => c.Candidate.UserId == frozen.UserId) is { } card) Assert.Equal(1, card.Candidate.AvailableHoursPerWeek);
            seen.AddRange(page.Items.Select(c => c.Candidate.UserId));
        }
        Assert.Equal(1004, seen.Count); Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.DoesNotContain(unseen.UserId, seen); Assert.Contains(removed, seen);
        var replay = await service.GetAsync(data.Leader, data.Project, data.Position, 20, firstCursor, filters);
        var replayAgain = await service.GetAsync(data.Leader, data.Project, data.Position, 20, firstCursor, filters);
        Assert.Equal(replay.Items.Select(c => c.Candidate.UserId), replayAgain.Items.Select(c => c.Candidate.UserId));
    }

    [Fact]
    public void Cursors_AreSigned_ContextBound_Expire_AndEvict()
    {
        var clock = new ProjectTestClock(DateTime.UtcNow); var sessions = new TeammateDiscoveryFeedSessions(clock);
        var user = Guid.NewGuid(); var project = Guid.NewGuid(); var position = Guid.NewGuid(); var filters = new TeammateDiscoveryFilters();
        var session = sessions.Create(user, project, position, clock.UtcNow.UtcDateTime, 20, filters, []);
        var cursor = sessions.Cursor(session, 0);
        Assert.Equal(session, sessions.Read(cursor, user, project, position, 20, filters).Session);
        foreach (var invalid in new[] { "", "bad", cursor + "A" }) Assert.Equal("INVALID_TEAMMATE_DISCOVERY_CURSOR", Assert.Throws<ProjectException>(() => sessions.Read(invalid, user, project, position, 20, filters)).ErrorCode);
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, Guid.NewGuid(), project, position, 20, filters));
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, Guid.NewGuid(), position, 20, filters));
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, project, Guid.NewGuid(), 20, filters));
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, project, position, 1, filters));
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, project, position, 20, filters with { SkillId = Guid.NewGuid() }));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        Assert.Equal("TEAMMATE_DISCOVERY_CURSOR_EXPIRED", Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, project, position, 20, filters)).ErrorCode);
        var recent = sessions.Create(user, project, position, clock.UtcNow.UtcDateTime, 20, filters, []);
        for (var i = 0; i < 4; i++) { clock.UtcNow = clock.UtcNow.AddSeconds(1); sessions.Create(user, project, position, clock.UtcNow.UtcDateTime, 20, filters, []); }
        Assert.Throws<ProjectException>(() => sessions.Read(sessions.Cursor(recent, 0), user, project, position, 20, filters));
    }

    [Fact]
    public void SnapshotBudgets_RejectOversizedAndEvictOldestGlobalSession()
    {
        var clock = new ProjectTestClock(DateTime.UtcNow); var sessions = new TeammateDiscoveryFeedSessions(clock);
        var user = Guid.NewGuid(); var project = Guid.NewGuid(); var position = Guid.NewGuid(); var filters = new TeammateDiscoveryFilters();
        var preview = new TeammateCandidatePreview(Guid.NewGuid(), "Candidate", null, null, null, null, null, [], 0, 0, "", "", "");
        var matching = PositionMatchScorer.Score(new(null, null, []), new(position, "Position", Guid.NewGuid(), "Role", "intermediate", 1, 0, "open", []), 10);
        var card = new TeammateCandidateCard(preview, null, matching);
        Assert.Equal("TEAMMATE_DISCOVERY_SNAPSHOT_TOO_LARGE", Assert.Throws<ProjectException>(() =>
            sessions.Create(user, project, position, clock.UtcNow.UtcDateTime, 20, filters, Enumerable.Repeat(card, 100_001).ToArray())).ErrorCode);
        var large = sessions.Create(user, project, position, clock.UtcNow.UtcDateTime, 20, filters, Enumerable.Repeat(card, 100_000).ToArray());
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sessions.Create(Guid.NewGuid(), project, position, clock.UtcNow.UtcDateTime, 20, filters, [card]);
        Assert.Throws<ProjectException>(() => sessions.Read(sessions.Cursor(large, 0), user, project, position, 20, filters));
        var oldest = sessions.Create(user, project, position, clock.UtcNow.UtcDateTime, 20, filters, []);
        for (var i = 0; i < 128; i++) { clock.UtcNow = clock.UtcNow.AddSeconds(1); sessions.Create(Guid.NewGuid(), project, position, clock.UtcNow.UtcDateTime, 20, filters, []); }
        Assert.Throws<ProjectException>(() => sessions.Read(sessions.Cursor(oldest, 0), user, project, position, 20, filters));
    }
}
