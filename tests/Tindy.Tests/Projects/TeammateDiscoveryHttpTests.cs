using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class TeammateDiscoveryHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Theory]
    [InlineData("optout")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("onboarding")]
    [InlineData("member")]
    [InlineData("application")]
    [InlineData("invitation")]
    [InlineData("cooldown")]
    [InlineData("skipped")]
    [InlineData("missing_profile")]
    public async Task Eligibility_IsFilteredByDatabase(string reason)
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var user = await db.Users.Include(u => u.Profile).SingleAsync(u => u.UserId == data.Applicant);
        switch (reason)
        {
            case "optout": user.Profile!.OpenToTeamInvitations = false; break;
            case "inactive": user.Status = "suspended"; break;
            case "deleted": user.DeletedAt = DateTime.UtcNow; break;
            case "onboarding": user.IsOnboardingCompleted = false; break;
            case "missing_profile": db.UserProfiles.Remove(user.Profile!); break;
            case "member": db.ProjectMembers.Add(new() { ProjectId = data.Project, UserId = data.Applicant, RoleTitle = "Member", JoinedAt = DateTime.UtcNow }); break;
            case "application": db.ProjectApplications.Add(new() { ApplicationId = Guid.NewGuid(), ProjectId = data.Project, PositionId = data.OtherPosition, ApplicantId = data.Applicant, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }); break;
            case "invitation":
            case "cooldown":
                var invitation = TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-1));
                invitation.PositionId = data.OtherPosition;
                if (reason == "cooldown") { invitation.Status = "declined"; invitation.RespondedAt = invitation.UpdatedAt = DateTime.UtcNow; }
                db.ProjectInvitations.Add(invitation); break;
            case "skipped": db.TeammateDiscoveryInteractions.Add(new() { ProjectId = data.Project, PositionId = data.Position, CandidateUserId = data.Applicant, Action = "skipped", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }); break;
        }
        await db.SaveChangesAsync();
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        var rows = await TeammateTestData.AllCandidates(leader, TeammateTestData.Feed(data));
        Assert.DoesNotContain(rows, c => c.GetProperty("candidate").GetProperty("user_id").GetGuid() == data.Applicant);
    }

    [Fact]
    public async Task FormerMembers_ElapsedHistory_AndOtherPositionSkips_AreEligibleWithMissingScoringData()
    {
        var data = await TeammateTestData.Seed(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.UserProfiles.Where(p => p.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(p => p.PreferredRoleId, (Guid?)null).SetProperty(p => p.AvailableHoursPerWeek, (int?)null));
            await db.UserSkills.Where(s => s.UserId == data.Applicant).ExecuteDeleteAsync();
            db.ProjectMembers.Add(new() { ProjectId = data.Project, UserId = data.Applicant, Status = "left", RoleTitle = "Former member", JoinedAt = DateTime.UtcNow });
            db.ProjectInvitations.Add(TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-169)));
            var declined = TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-200));
            declined.Status = "declined"; declined.RespondedAt = declined.UpdatedAt = DateTime.UtcNow.AddHours(-169);
            db.ProjectInvitations.Add(declined);
            db.TeammateDiscoveryInteractions.Add(new() { ProjectId = data.Project, PositionId = data.OtherPosition, CandidateUserId = data.Applicant, Action = "skipped", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.Projects.Add(RecruitmentData.Project(data.Applicant, data.Role, Guid.NewGuid().ToString()));
            await db.SaveChangesAsync();
        }
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        var rows = await TeammateTestData.AllCandidates(leader, TeammateTestData.Feed(data));
        var card = rows.Single(c => c.GetProperty("candidate").GetProperty("user_id").GetGuid() == data.Applicant);
        Assert.True(card.GetProperty("matching").GetProperty("data_incomplete").GetBoolean());
        Assert.Equal(0, card.GetProperty("matching").GetProperty("match_score").GetInt32());
        var unknown = await RecruitmentHttp.Data(await leader.GetAsync(TeammateTestData.Feed(data) + "?skill_id=" + Guid.NewGuid()));
        Assert.Empty(unknown.GetProperty("items").EnumerateArray());
        await ProjectApplicationHttpTests.Problem(await leader.GetAsync(TeammateTestData.Feed(data) + "?cursor="), HttpStatusCode.BadRequest, "INVALID_TEAMMATE_DISCOVERY_CURSOR");
    }

    [Fact]
    public async Task HttpOwnership_QueryValidation_Privacy_AndSelectedPositionMatching()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        using var other = RecruitmentHttp.Client(factory, data.Outsider);
        using var anonymous = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(TeammateTestData.Feed(data))).StatusCode);
        await ProjectApplicationHttpTests.Problem(await other.GetAsync(TeammateTestData.Feed(data)), HttpStatusCode.Forbidden, "PROJECT_ACCESS_DENIED");
        await ProjectApplicationHttpTests.Problem(await leader.GetAsync(TeammateTestData.Feed(data) + "?min_score=80"), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await ProjectApplicationHttpTests.Problem(await leader.GetAsync(TeammateTestData.Feed(data) + "?sort_by=newest"), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var second = await TeammateTestData.Seed(factory);
        await ProjectApplicationHttpTests.Problem(await leader.GetAsync($"/api/projects/{data.Project}/positions/{second.Position}/candidates"), HttpStatusCode.NotFound, "POSITION_NOT_FOUND");
        async Task<System.Text.Json.JsonElement> FindCandidate(string path)
        {
            string? cursor = null;
            do
            {
                var page = await RecruitmentHttp.Data(await leader.GetAsync(path + (cursor == null ? "" : "?cursor=" + Uri.EscapeDataString(cursor))));
                foreach (var item in page.GetProperty("items").EnumerateArray())
                    if (item.GetProperty("candidate").GetProperty("user_id").GetGuid() == data.Applicant) return item;
                cursor = page.GetProperty("next_cursor").GetString();
            } while (cursor != null);
            throw new Xunit.Sdk.XunitException("Eligible candidate was missing from the complete feed.");
        }
        var card = await FindCandidate(TeammateTestData.Feed(data));
        Assert.Equal(100, card.GetProperty("matching").GetProperty("match_score").GetInt32());
        Assert.Equal(data.Position, card.GetProperty("matching").GetProperty("best_position").GetProperty("position_id").GetGuid());
        Assert.False(card.GetProperty("candidate").TryGetProperty("bio", out _));
        var otherMatch = (await FindCandidate($"/api/projects/{data.Project}/positions/{data.OtherPosition}/candidates")).GetProperty("matching");
        Assert.Equal(40, otherMatch.GetProperty("match_score").GetInt32());
        Assert.Equal(data.OtherPosition, otherMatch.GetProperty("best_position").GetProperty("position_id").GetGuid());
    }
}
