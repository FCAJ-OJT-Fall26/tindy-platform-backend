using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class DiscoveryFeedTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public DiscoveryFeedTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ThousandProjects_RankBeyondFirst50_WithStableUniquePaginationAndBatchedQueries()
    {
        using var scope = _factory.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var leader = RecruitmentData.User(); var viewer = RecruitmentData.User();
        var roles = await seed.ProfessionalRoles.OrderBy(r => r.Name).Take(2).Select(r => r.ProfessionalRoleId).ToArrayAsync();
        viewer.Profile = new() { UserId = viewer.UserId, DisplayName = "Viewer", PreferredRoleId = roles[0], AvailableHoursPerWeek = 10, CreatedAt = DateTime.UtcNow };
        seed.Users.AddRange(leader, viewer);
        var category = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var projects = Enumerable.Range(0, 1000).Select(i => RecruitmentData.Project(leader.UserId, roles[i == 0 ? 0 : 1], category,
            now.AddMinutes(i - 1001))).ToArray();
        seed.Projects.AddRange(projects);
        await seed.SaveChangesAsync();

        var counter = new QueryCounter();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(seed.Database.GetConnectionString()).AddInterceptors(counter).Options);
        var clock = TimeProvider.System;
        var sessions = new DiscoveryFeedSessions(clock);
        var service = new DiscoveryService(new DiscoveryRepository(db), sessions, clock);
        var page = await service.GetAsync(viewer.UserId, 20, null, new(category));
        Assert.InRange(counter.Count, 1, 6);
        Assert.Equal(projects[0].ProjectId, page.Items[0].ProjectId); // oldest, outside newest 50
        Assert.Equal(12, page.Items.Count(p => p.FeedBucket == "best_match"));
        Assert.Equal(5, page.Items.Count(p => p.FeedBucket == "new_projects"));
        Assert.Equal(3, page.Items.Count(p => p.FeedBucket == "explore"));
        Assert.Equal("feed-v1", page.FeedVersion);
        var firstCursor = page.NextCursor!;
        var seen = page.Items.Select(p => p.ProjectId).ToList();

        var fresh = RecruitmentData.Project(leader.UserId, roles[0], category, DateTime.UtcNow);
        seed.Projects.Add(fresh); await seed.SaveChangesAsync();
        while (page.HasMore)
        {
            counter.Count = 0;
            page = await service.GetAsync(viewer.UserId, 20, page.NextCursor, new(category));
            Assert.InRange(counter.Count, 1, 4);
            seen.AddRange(page.Items.Select(p => p.ProjectId));
        }
        Assert.Equal(1000, seen.Count);
        Assert.Equal(1000, seen.Distinct().Count());
        Assert.DoesNotContain(fresh.ProjectId, seen);
        var refresh = await service.GetAsync(viewer.UserId, 20, null, new(category));
        Assert.Contains(refresh.Items, p => p.ProjectId == fresh.ProjectId);
        var repeated = await service.GetAsync(viewer.UserId, 20, firstCursor, new(category));
        var repeatedAgain = await service.GetAsync(viewer.UserId, 20, firstCursor, new(category));
        Assert.Equal(repeated.Items.Select(p => p.ProjectId), repeatedAgain.Items.Select(p => p.ProjectId));
        Assert.All(refresh.Items, p => Assert.Equal(0, p.BestPosition.FilledCount));
        Assert.Equal(50, (await service.GetAsync(viewer.UserId, 100, null, new(category))).Items.Count);
        Assert.Single((await service.GetAsync(viewer.UserId, 0, null, new(category))).Items);
    }

    [Fact]
    public void Cursor_RejectsMalformedTamperedCrossAccountChangedFiltersAndExpiredSnapshots()
    {
        var clock = new ProjectTestClock(DateTime.UtcNow);
        var sessions = new DiscoveryFeedSessions(clock);
        var user = Guid.NewGuid(); var filters = new DiscoveryFilters("Web");
        var session = sessions.Create(user, clock.UtcNow.UtcDateTime, 20, filters, []);
        var cursor = sessions.Cursor(session, 0);
        Assert.Equal(session, sessions.Read(cursor, user, 20, filters).Session);
        foreach (var invalid in new[] { "", "nonsense", cursor + "A", new string('a', 500) })
            Assert.Equal("INVALID_DISCOVERY_CURSOR", Assert.Throws<ProjectException>(() => sessions.Read(invalid, user, 20, filters)).ErrorCode);
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, Guid.NewGuid(), 20, filters));
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, 50, filters));
        Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, 20, new("Other")));
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        Assert.Equal("DISCOVERY_CURSOR_EXPIRED", Assert.Throws<ProjectException>(() => sessions.Read(cursor, user, 20, filters)).ErrorCode);
    }

    [Fact]
    public async Task ExistingSnapshot_RechecksSkipAndPositionAvailability_WithoutRepeats()
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var leader = RecruitmentData.User(); var viewer = RecruitmentData.User(); db.Users.AddRange(leader, viewer);
        var role = await db.ProfessionalRoles.Select(r => r.ProfessionalRoleId).FirstAsync();
        var category = Guid.NewGuid().ToString();
        var projects = Enumerable.Range(0, 8).Select(_ => RecruitmentData.Project(leader.UserId, role, category)).ToArray();
        foreach (var project in projects) project.Positions.Add(new() { PositionId = Guid.NewGuid(), ProjectId = project.ProjectId, RoleId = role, Title = "Alternative", CreatedAt = DateTime.UtcNow });
        db.Projects.AddRange(projects); await db.SaveChangesAsync();
        var repository = new DiscoveryRepository(db);
        var service = new DiscoveryService(repository, new DiscoveryFeedSessions(TimeProvider.System), TimeProvider.System);
        var first = await service.GetAsync(viewer.UserId, 2, null, new(category));
        var peek = await service.GetAsync(viewer.UserId, 2, first.NextCursor, new(category));
        await db.ProjectPositions.Where(p => p.PositionId == peek.Items[0].BestPosition.PositionId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "closed"));
        await new DiscoveryInteractionRepository(db).SetAsync(viewer.UserId, peek.Items[1].ProjectId, false, true, DateTime.UtcNow);
        var next = await service.GetAsync(viewer.UserId, 2, first.NextCursor, new(category));
        Assert.Equal(2, next.Items.Count);
        Assert.DoesNotContain(next.Items, p => peek.Items.Any(old => old.ProjectId == p.ProjectId));
        Assert.DoesNotContain(next.Items, p => first.Items.Any(old => old.ProjectId == p.ProjectId));
        // The project with a now-closed best position still has another open
        // position; a refresh can score that alternative.
        var current = await repository.GetCandidatesAsync(viewer.UserId, DateTime.UtcNow, DateTime.UtcNow, new(category));
        Assert.Contains(current, p => p.ProjectId == peek.Items[0].ProjectId);
    }
}

internal static class RecruitmentData
{
    public static User User() => new()
    {
        UserId = Guid.NewGuid(), Status = "active", IsOnboardingCompleted = true,
        CreatedAt = DateTime.UtcNow, PrimaryEmail = "private-canary@example.com"
    };

    public static Project Project(Guid leader, Guid role, string category, DateTime? created = null, int required = 2, int capacity = 5)
    {
        var id = Guid.NewGuid();
        return new()
        {
            ProjectId = id, LeaderId = leader, Name = "Recruitment Project", Description = "A sufficiently long project description.",
            Category = category, MaxTeamCapacity = capacity, CreatedAt = created ?? DateTime.UtcNow,
            RecruitmentClosesAt = DateTime.UtcNow.AddDays(7),
            Members = [new() { ProjectId = id, UserId = leader, IsLeader = true, RoleTitle = "Project Lead", JoinedAt = DateTime.UtcNow }],
            Positions = [new() { ProjectId = id, PositionId = Guid.NewGuid(), RoleId = role, Title = "Developer", RequiredCount = required, CreatedAt = DateTime.UtcNow }]
        };
    }
}

internal sealed class QueryCounter : DbCommandInterceptor
{
    public int Count;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }
}
