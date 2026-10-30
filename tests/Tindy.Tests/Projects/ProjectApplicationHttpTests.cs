using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ProjectApplicationHttpTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public ProjectApplicationHttpTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ReapplyImmediately_SamePosition_AfterRejectAndWithdraw_PreservesHistoryAndOnePending()
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var ids = new List<Guid>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var application = await Apply(member, test.Project, test.Position);
            ids.Add(application);
            await Problem(await member.PostAsJsonAsync(Url(test.Project), new { position_id = test.OtherPosition }), HttpStatusCode.Conflict, "APPLICATION_ALREADY_PENDING");
            if (attempt % 2 == 0)
                await RecruitmentHttp.Data(await leader.PostAsync($"{Url(test.Project)}/{application}/reject", null));
            else
                await RecruitmentHttp.Data(await member.PostAsync($"/api/me/project-applications/{application}/withdraw", null));
        }
        var latest = await Apply(member, test.Project, test.Position); ids.Add(latest);
        var mine = await RecruitmentHttp.Data(await member.GetAsync("/api/me/project-applications"));
        Assert.Equal(7, mine.GetProperty("pagination").GetProperty("total_items").GetInt32());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var history = await db.ProjectApplications.Where(a => a.ProjectId == test.Project).ToArrayAsync();
        Assert.Equal(7, history.Length); Assert.Equal(7, ids.Distinct().Count());
        Assert.Equal(3, history.Count(a => a.Status == "rejected"));
        Assert.Equal(3, history.Count(a => a.Status == "withdrawn"));
        Assert.Equal(latest, Assert.Single(history, a => a.Status == "pending").ApplicationId);
        Assert.All(history.Where(a => a.Status == "rejected"), a => { Assert.NotNull(a.ReviewedAt); Assert.Equal(test.Leader, a.ReviewedBy); });
        Assert.Equal(0, (await db.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).FilledCount);
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == test.Project));
        var review = await RecruitmentHttp.Data(await leader.GetAsync(Url(test.Project)));
        Assert.True(review.GetProperty("items")[0].GetProperty("matching").GetProperty("data_incomplete").GetBoolean());
        var projects = await RecruitmentHttp.Data(await leader.GetAsync("/api/me/projects"));
        Assert.Equal(1, projects.GetProperty("leading_projects").EnumerateArray().Single(p => p.GetProperty("project_id").GetGuid() == test.Project).GetProperty("pending_applications_count").GetInt32());
        Assert.Empty((await RecruitmentHttp.Data(await member.GetAsync($"/api/discovery/projects?category={test.Category}"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task OtherProjectLeader_CanApplyAndJoin_AndExistingPendingCanBeAcceptedAfterDeadline()
    {
        var test = await ApplicationTestData.Seed(_factory);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.Projects.Add(RecruitmentData.Project(test.Applicant, test.Role, Guid.NewGuid().ToString()));
            await db.SaveChangesAsync();
        }
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        Assert.Single((await RecruitmentHttp.Data(await member.GetAsync($"/api/discovery/projects?category={test.Category}"))).GetProperty("items").EnumerateArray());
        var application = await Apply(member, test.Project, test.Position);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.Projects.Where(p => p.ProjectId == test.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecruitmentClosesAt, DateTime.UtcNow.AddMinutes(-1)));
        }
        var accepted = await RecruitmentHttp.Data(await leader.PostAsync($"{Url(test.Project)}/{application}/accept", null));
        Assert.Equal("accepted", accepted.GetProperty("status").GetString());
        await Problem(await leader.PostAsync($"{Url(test.Project)}/{application}/accept", null), HttpStatusCode.Conflict, "APPLICATION_NOT_PENDING");
        await Problem(await leader.PostAsync($"{Url(test.Project)}/{application}/reject", null), HttpStatusCode.Conflict, "APPLICATION_NOT_PENDING");
        await Problem(await member.PostAsync($"/api/me/project-applications/{application}/withdraw", null), HttpStatusCode.Conflict, "APPLICATION_NOT_PENDING");
        var projects = await RecruitmentHttp.Data(await member.GetAsync("/api/me/projects"));
        Assert.Contains(projects.GetProperty("joined_projects").EnumerateArray(), p => p.GetProperty("project_id").GetGuid() == test.Project);
        using var scope2 = _factory.Services.CreateScope();
        var check = scope2.ServiceProvider.GetRequiredService<TindyDbContext>();
        var membership = await check.ProjectMembers.SingleAsync(m => m.ProjectId == test.Project && m.UserId == test.Applicant);
        Assert.Equal(test.Position, membership.PositionId); Assert.False(membership.IsLeader); Assert.Equal("Developer", membership.RoleTitle);
        Assert.Null((await check.ProjectMembers.SingleAsync(m => m.ProjectId == test.Project && m.IsLeader)).PositionId);
        Assert.Equal(1, (await check.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).FilledCount);
    }

    [Fact]
    public async Task LeaderReview_IsPrivateAndScoresAppliedPosition_NotBestProjectPosition()
    {
        var test = await ApplicationTestData.Seed(_factory, withProfile: true);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var outsider = RecruitmentHttp.Client(_factory, test.Outsider);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var application = await Apply(member, test.Project, test.OtherPosition);
        await Problem(await outsider.GetAsync(Url(test.Project)), HttpStatusCode.Forbidden, "PROJECT_ACCESS_DENIED");
        await Problem(await outsider.PostAsync($"{Url(test.Project)}/{application}/accept", null), HttpStatusCode.Forbidden, "PROJECT_ACCESS_DENIED");
        await Problem(await outsider.PostAsync($"/api/me/project-applications/{application}/withdraw", null), HttpStatusCode.NotFound, "APPLICATION_NOT_FOUND");
        Assert.Empty((await RecruitmentHttp.Data(await outsider.GetAsync("/api/me/project-applications"))).GetProperty("items").EnumerateArray());
        var review = await RecruitmentHttp.Data(await leader.GetAsync($"{Url(test.Project)}?position_id={test.OtherPosition}&sort_by=match"));
        var item = Assert.Single(review.GetProperty("items").EnumerateArray());
        Assert.Equal(test.OtherPosition, item.GetProperty("matching").GetProperty("best_position").GetProperty("position_id").GetGuid());
        Assert.Equal(40, item.GetProperty("matching").GetProperty("match_score").GetInt32());
        Assert.Equal($"/api/users/{test.Applicant}/profile", item.GetProperty("applicant").GetProperty("profile_url").GetString());
        await RecruitmentHttp.Data(await leader.GetAsync($"/api/users/{test.Applicant}/profile"));
        await RecruitmentHttp.Data(await leader.GetAsync($"/api/users/{test.Applicant}/profile/experiences"));
        await RecruitmentHttp.Data(await leader.GetAsync($"/api/users/{test.Applicant}/profile/certificates"));
        await Problem(await leader.GetAsync(Url(test.Project) + "?status=invalid"), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("archived")]
    [InlineData("full")]
    [InlineData("closed_position")]
    [InlineData("active_member")]
    [InlineData("own_project")]
    public async Task Apply_RechecksEligibility(string reason)
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var userId = test.Applicant;
        switch (reason)
        {
            case "expired": await db.Projects.Where(p => p.ProjectId == test.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecruitmentClosesAt, DateTime.UtcNow.AddSeconds(-1))); break;
            case "archived": await db.Projects.Where(p => p.ProjectId == test.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.ArchivedAt, DateTime.UtcNow)); break;
            case "full": await db.Projects.Where(p => p.ProjectId == test.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.MaxTeamCapacity, 1)); break;
            case "closed_position": await db.ProjectPositions.Where(p => p.PositionId == test.Position).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "closed")); break;
            case "active_member": db.ProjectMembers.Add(new() { ProjectId = test.Project, UserId = test.Applicant, RoleTitle = "Existing", JoinedAt = DateTime.UtcNow }); await db.SaveChangesAsync(); break;
            case "own_project": userId = test.Leader; break;
        }
        using var client = RecruitmentHttp.Client(_factory, userId);
        using var response = await client.PostAsJsonAsync(Url(test.Project), new { position_id = test.Position });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await db.ProjectApplications.Where(a => a.ProjectId == test.Project).ToArrayAsync());
    }

    [Theory]
    [InlineData("left")]
    [InlineData("removed")]
    public async Task Acceptance_ReactivatesCompositeMembership_WithoutDuplicateOrDoubleIncrement(string state)
    {
        var test = await ApplicationTestData.Seed(_factory);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.ProjectMembers.Add(new() { ProjectId = test.Project, UserId = test.Applicant, PositionId = test.Position, Status = state, RoleTitle = "Old role", JoinedAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var application = await Apply(member, test.Project, test.Position);
        await RecruitmentHttp.Data(await leader.PostAsync($"{Url(test.Project)}/{application}/accept", null));
        await Problem(await member.PostAsJsonAsync(Url(test.Project), new { position_id = test.OtherPosition }), HttpStatusCode.Conflict, "PROJECT_NOT_ELIGIBLE");
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(2, await db2.ProjectMembers.CountAsync(m => m.ProjectId == test.Project && m.Status == "active"));
        Assert.Equal(1, (await db2.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).FilledCount);
    }

    [Fact]
    public async Task PositionHistoryAndApprovedMembers_PreventDeleteOrInvalidCapacityEdits()
    {
        var test = await ApplicationTestData.Seed(_factory, required: 2);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var application = await Apply(member, test.Project, test.Position);
        await Problem(await leader.DeleteAsync($"/api/projects/{test.Project}/positions/{test.Position}"), HttpStatusCode.Conflict, "POSITION_IN_USE");
        await RecruitmentHttp.Data(await leader.PostAsync($"{Url(test.Project)}/{application}/accept", null));
        using var outsider = RecruitmentHttp.Client(_factory, test.Outsider);
        var next = await Apply(outsider, test.Project, test.Position);
        await RecruitmentHttp.Data(await leader.PostAsync($"{Url(test.Project)}/{next}/accept", null));
        await Problem(await leader.PutAsJsonAsync($"/api/projects/{test.Project}/positions/{test.Position}", new { required_count = 1 }), HttpStatusCode.Conflict, "POSITION_CAPACITY_EXCEEDED");
        await Problem(await leader.DeleteAsync($"/api/projects/{test.Project}/positions/{test.Position}"), HttpStatusCode.Conflict, "POSITION_IN_USE");
        var request = new { name = "Changed Project", description = "A sufficiently long changed description.", category = "Web", difficulty_level = "intermediate", expected_commitment_hours = 10, duration = "3 months", max_team_capacity = 2, goals = Array.Empty<string>() };
        await Problem(await leader.PutAsJsonAsync($"/api/projects/{test.Project}", request), HttpStatusCode.BadRequest, "PROJECT_CAPACITY_EXCEEDED");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal("filled", (await db.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).Status);
    }

    [Theory]
    [InlineData("full", "PROJECT_CAPACITY_EXCEEDED")]
    [InlineData("closed", "POSITION_UNAVAILABLE")]
    [InlineData("status", "PROJECT_NOT_RECRUITING")]
    [InlineData("inactive_applicant", "APPLICANT_UNAVAILABLE")]
    public async Task AcceptanceFailure_LeavesPendingAndSlotsUnchanged(string reason, string code)
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        using var leader = RecruitmentHttp.Client(_factory, test.Leader);
        var application = await Apply(member, test.Project, test.Position);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        switch (reason)
        {
            case "full": await db.Projects.Where(p => p.ProjectId == test.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.MaxTeamCapacity, 1)); break;
            case "closed": await db.ProjectPositions.Where(p => p.PositionId == test.Position).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "closed")); break;
            case "status": await db.Projects.Where(p => p.ProjectId == test.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "completed")); break;
            case "inactive_applicant": await db.Users.Where(u => u.UserId == test.Applicant).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "suspended")); break;
        }
        await Problem(await leader.PostAsync($"{Url(test.Project)}/{application}/accept", null), HttpStatusCode.Conflict, code);
        Assert.Equal("pending", (await db.ProjectApplications.SingleAsync(a => a.ApplicationId == application)).Status);
        Assert.Equal(0, (await db.ProjectPositions.SingleAsync(p => p.PositionId == test.Position)).FilledCount);
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == test.Project));
    }

    [Fact]
    public async Task ApplicationBurstLimit_IsPerUser_AndReturnsExplicit429()
    {
        var test = await ApplicationTestData.Seed(_factory);
        using var member = RecruitmentHttp.Client(_factory, test.Applicant);
        for (var i = 0; i < 120; i++)
        {
            using var response = await member.PostAsJsonAsync(Url(test.Project), new { position_id = Guid.Empty });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await Problem(await member.PostAsJsonAsync(Url(test.Project), new { position_id = Guid.Empty }), HttpStatusCode.TooManyRequests, "APPLICATION_RATE_LIMITED");
        using var another = RecruitmentHttp.Client(_factory, test.Outsider);
        using var unaffected = await another.PostAsJsonAsync(Url(test.Project), new { position_id = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, unaffected.StatusCode);
    }

    internal static string Url(Guid project) => $"/api/projects/{project}/applications";
    internal static async Task<Guid> Apply(HttpClient client, Guid project, Guid position)
    {
        using var response = await client.PostAsJsonAsync(Url(project), new { position_id = position, message = "I would like to contribute." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await RecruitmentHttp.Data(response);
        Assert.Equal("pending", data.GetProperty("status").GetString());
        return data.GetProperty("application_id").GetGuid();
    }
    internal static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, raw);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, JsonDocument.Parse(raw).RootElement.GetProperty("error_code").GetString());
    }
}

internal sealed record ApplicationCase(Guid Project, Guid Leader, Guid Applicant, Guid Outsider, Guid Position, Guid OtherPosition, Guid Role, string Category);
internal static class ApplicationTestData
{
    public static async Task<ApplicationCase> Seed(TindyApiFactory factory, int required = 2, int capacity = 5, bool withProfile = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var leader = RecruitmentData.User(); var member = RecruitmentData.User(); var outsider = RecruitmentData.User();
        var roles = await db.ProfessionalRoles.OrderBy(r => r.Name).Take(2).Select(r => r.ProfessionalRoleId).ToArrayAsync();
        if (withProfile) member.Profile = new() { UserId = member.UserId, DisplayName = "Applicant", PreferredRoleId = roles[0], AvailableHoursPerWeek = 10, CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(leader, member, outsider);
        var category = Guid.NewGuid().ToString();
        var project = RecruitmentData.Project(leader.UserId, roles[0], category, required: required, capacity: capacity);
        var other = new ProjectPosition { ProjectId = project.ProjectId, PositionId = Guid.NewGuid(), RoleId = roles[1], Title = "Designer", RequiredCount = required, CreatedAt = DateTime.UtcNow };
        project.Positions.Add(other);
        db.Projects.Add(project); await db.SaveChangesAsync();
        return new(project.ProjectId, leader.UserId, member.UserId, outsider.UserId, project.Positions.First().PositionId, other.PositionId, roles[0], category);
    }
}
