using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

public sealed class ProjectInvitationHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("inactive", HttpStatusCode.Forbidden)]
    [InlineData("incomplete", HttpStatusCode.Forbidden)]
    public async Task NewRoutes_KeepExistingAccountAndOnboardingPolicies(string state, HttpStatusCode expected)
    {
        var data = await TeammateTestData.Seed(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            if (state == "inactive") await db.Users.Where(u => u.UserId == data.Leader).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "suspended"));
            if (state == "incomplete") await db.Users.Where(u => u.UserId == data.Leader).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsOnboardingCompleted, false));
        }
        using var client = state == "anonymous" ? factory.CreateHttpsClient() : RecruitmentHttp.Client(factory, data.Leader);
        foreach (var path in new[] { TeammateTestData.Feed(data), TeammateInteractionHttpTests.Path(data), LeaderPath(data), "/api/me/project-invitations" })
            Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { LeaderPath(data), $"{LeaderPath(data)}/{Guid.NewGuid()}/cancel", MemberPath(Guid.NewGuid(), "accept"), MemberPath(Guid.NewGuid(), "decline") })
            Assert.Equal(expected, (await client.PostAsync(path, null)).StatusCode);
    }

    [Fact]
    public async Task SkippedCandidate_CanBeExplicitlyInvited_AndOptInCanBeRestoredBeforeAccept()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        await RecruitmentHttp.Data(await leader.PutAsJsonAsync(TeammateInteractionHttpTests.Path(data, data.Applicant), new { action = "skipped" }));
        var id = await Send(leader, data);
        Assert.Equal(HttpStatusCode.OK, (await member.PatchAsJsonAsync("/api/me/profile", new { open_to_team_invitations = false })).StatusCode);
        await ProjectApplicationHttpTests.Problem(await member.PostAsync(MemberPath(id, "accept"), null), HttpStatusCode.Conflict, "CANDIDATE_UNAVAILABLE");
        Assert.Equal(HttpStatusCode.OK, (await member.PatchAsJsonAsync("/api/me/profile", new { open_to_team_invitations = true })).StatusCode);
        await RecruitmentHttp.Data(await member.PostAsync(MemberPath(id, "accept"), null));
    }
    internal static string LeaderPath(ApplicationCase data) => $"/api/projects/{data.Project}/invitations";
    internal static string MemberPath(Guid id, string action) => $"/api/me/project-invitations/{id}/{action}";
    internal static async Task<Guid> Send(HttpClient leader, ApplicationCase data, Guid? recipient = null, Guid? position = null)
    {
        var response = await leader.PostAsJsonAsync(LeaderPath(data), new { position_id = position ?? data.Position, invited_user_id = recipient ?? data.Applicant, message = "  Join our team  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = await RecruitmentHttp.Data(response);
        Assert.Equal("pending", row.GetProperty("status").GetString());
        Assert.Equal("Join our team", row.GetProperty("message").GetString());
        Assert.Equal(TimeSpan.FromHours(168), row.GetProperty("expires_at").GetDateTime() - row.GetProperty("created_at").GetDateTime());
        return row.GetProperty("invitation_id").GetGuid();
    }

    [Fact]
    public async Task Send_Cancel_Reinvite_Decline_AndHistory_PreserveRowsAndDoNotReserveSlots()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var first = await Send(leader, data);
        await ProjectApplicationHttpTests.Problem(await leader.PostAsJsonAsync(LeaderPath(data), new { position_id = data.OtherPosition, invited_user_id = data.Applicant }), HttpStatusCode.Conflict, "INVITATION_ALREADY_PENDING");
        await ProjectApplicationHttpTests.Problem(await member.PostAsJsonAsync(ProjectApplicationHttpTests.Url(data.Project), new { position_id = data.OtherPosition }), HttpStatusCode.Conflict, "INVITATION_ALREADY_PENDING");
        await RecruitmentHttp.Data(await leader.PostAsync($"{LeaderPath(data)}/{first}/cancel", null));
        await ProjectApplicationHttpTests.Problem(await leader.PostAsync($"{LeaderPath(data)}/{first}/cancel", null), HttpStatusCode.Conflict, "INVITATION_NOT_PENDING");
        var second = await Send(leader, data, position: data.OtherPosition);
        Assert.NotEqual(first, second);
        await RecruitmentHttp.Data(await member.PostAsync(MemberPath(second, "decline"), null));
        await ProjectApplicationHttpTests.Problem(await member.PostAsync(MemberPath(second, "decline"), null), HttpStatusCode.Conflict, "INVITATION_NOT_PENDING");
        await ProjectApplicationHttpTests.Problem(await leader.PostAsJsonAsync(LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant }), HttpStatusCode.Conflict, "INVITATION_COOLDOWN_ACTIVE");
        var history = await RecruitmentHttp.Data(await member.GetAsync($"/api/me/project-invitations?project_id={data.Project}&page_size=1"));
        Assert.Equal(2, history.GetProperty("pagination").GetProperty("total_items").GetInt32());
        var leaderHistory = await RecruitmentHttp.Data(await leader.GetAsync(LeaderPath(data) + "?status=declined"));
        Assert.Single(leaderHistory.GetProperty("items").EnumerateArray());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project));
        Assert.Equal(0, await db.ProjectPositions.Where(p => p.ProjectId == data.Project).SumAsync(p => p.FilledCount));
        // An Invitation decline does not create an Application cooldown.
        await ProjectApplicationHttpTests.Apply(member, data.Project, data.Position);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("removed")]
    public async Task Accept_AfterDeadline_ReactivatesMembership_UsesCurrentTitle_AndDoesNotRequireSkills(string formerStatus)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await Send(leader, data);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.ProjectMembers.Add(new() { ProjectId = data.Project, UserId = data.Applicant, PositionId = data.OtherPosition, Status = formerStatus, RoleTitle = "Old role", JoinedAt = DateTime.UtcNow.AddDays(-30) });
            await db.SaveChangesAsync();
            await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecruitmentClosesAt, DateTime.UtcNow.AddMinutes(-1)));
            await db.ProjectPositions.Where(p => p.PositionId == data.Position).ExecuteUpdateAsync(s => s.SetProperty(p => p.Title, "Current role title"));
            await db.UserSkills.Where(s => s.UserId == data.Applicant).ExecuteDeleteAsync();
            await db.UserProfiles.Where(p => p.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(p => p.AvailableHoursPerWeek, 1));
        }
        var result = await RecruitmentHttp.Data(await member.PostAsync(MemberPath(id, "accept"), null));
        Assert.Equal("accepted", result.GetProperty("status").GetString());
        await ProjectApplicationHttpTests.Problem(await member.PostAsync(MemberPath(id, "accept"), null), HttpStatusCode.Conflict, "INVITATION_NOT_PENDING");
        using var check = factory.Services.CreateScope(); var database = check.ServiceProvider.GetRequiredService<TindyDbContext>();
        var membership = await database.ProjectMembers.SingleAsync(m => m.ProjectId == data.Project && m.UserId == data.Applicant);
        Assert.Equal("active", membership.Status); Assert.Equal(data.Position, membership.PositionId); Assert.False(membership.IsLeader);
        Assert.Equal("Current role title", membership.RoleTitle);
        Assert.Equal(1, (await database.ProjectPositions.SingleAsync(p => p.PositionId == data.Position)).FilledCount);
        Assert.Empty(await database.ProjectApplications.Where(a => a.ProjectId == data.Project).ToListAsync());
        var mine = await RecruitmentHttp.Data(await member.GetAsync("/api/me/projects"));
        Assert.Contains(mine.GetProperty("joined_projects").EnumerateArray(), p => p.GetProperty("project_id").GetGuid() == data.Project);
        await ProjectApplicationHttpTests.Problem(await leader.DeleteAsync($"/api/projects/{data.Project}/positions/{data.Position}"), HttpStatusCode.Conflict, "POSITION_IN_USE");
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task EffectiveExpiry_ReadsDoNotWrite_ButLifecycleConflictCommitsExpiration(string action)
    {
        var data = await TeammateTestData.Seed(factory);
        var invitation = TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-169));
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>(); db.ProjectInvitations.Add(invitation); await db.SaveChangesAsync(); }
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var list = await RecruitmentHttp.Data(await member.GetAsync($"/api/me/project-invitations?project_id={data.Project}&status=expired"));
        Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("expired", list.GetProperty("items")[0].GetProperty("status").GetString());
        using (var scope = factory.Services.CreateScope()) Assert.Equal("pending", (await scope.ServiceProvider.GetRequiredService<TindyDbContext>().ProjectInvitations.SingleAsync(i => i.InvitationId == invitation.InvitationId)).Status);
        var response = action == "cancel" ? await leader.PostAsync($"{LeaderPath(data)}/{invitation.InvitationId}/cancel", null) : await member.PostAsync(MemberPath(invitation.InvitationId, action), null);
        await ProjectApplicationHttpTests.Problem(response, HttpStatusCode.Conflict, "INVITATION_EXPIRED");
        using var final = factory.Services.CreateScope(); var row = await final.ServiceProvider.GetRequiredService<TindyDbContext>().ProjectInvitations.SingleAsync(i => i.InvitationId == invitation.InvitationId);
        Assert.Equal("expired", row.Status); Assert.Equal(row.ExpiresAt, row.UpdatedAt); Assert.Null(row.RespondedAt);
        await Send(leader, data); // Expiration has no cooldown and history is preserved.
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SendAndApply_NormalizeElapsedRows_EvenWhenAdmissionFails(bool send, bool eligible)
    {
        var data = await TeammateTestData.Seed(factory);
        var old = TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-169));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>(); db.ProjectInvitations.Add(old); await db.SaveChangesAsync();
            if (!eligible) await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "archived"));
        }
        using var caller = RecruitmentHttp.Client(factory, send ? data.Leader : data.Applicant);
        var response = send ? await caller.PostAsJsonAsync(LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant }) : await caller.PostAsJsonAsync(ProjectApplicationHttpTests.Url(data.Project), new { position_id = data.Position });
        if (eligible) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else await ProjectApplicationHttpTests.Problem(response, HttpStatusCode.Conflict, "PROJECT_NOT_ELIGIBLE");
        using var final = factory.Services.CreateScope(); var check = final.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal("expired", (await check.ProjectInvitations.SingleAsync(i => i.InvitationId == old.InvitationId)).Status);
    }

    [Fact]
    public async Task DeclineCooldown_ExactBoundary_AndExpiration_UseServerClock()
    {
        var data = await TeammateTestData.Seed(factory);
        var now = DateTime.UtcNow; now = now.AddTicks(-(now.Ticks % 10));
        var clock = new ProjectTestClock(now);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecruitmentClosesAt, now.AddDays(30)));
        var service = new ProjectInvitationService(new ProjectInvitationRepository(db, clock));
        var first = await service.SendAsync(data.Leader, data.Project, new(data.Position, data.Applicant, null));
        clock.UtcNow = clock.UtcNow.AddHours(1);
        var declined = await service.ChangeAsync(data.Applicant, null, first.InvitationId, "decline");
        clock.UtcNow = new DateTimeOffset(declined.RespondedAt!.Value.AddHours(168).AddTicks(-10));
        Assert.Equal("INVITATION_COOLDOWN_ACTIVE", (await Assert.ThrowsAsync<ProjectException>(() => service.SendAsync(data.Leader, data.Project, new(data.OtherPosition, data.Applicant, null)))).ErrorCode);
        clock.UtcNow = clock.UtcNow.AddTicks(10);
        var second = await service.SendAsync(data.Leader, data.Project, new(data.OtherPosition, data.Applicant, null));
        Assert.NotEqual(first.InvitationId, second.InvitationId);
        clock.UtcNow = new DateTimeOffset(second.ExpiresAt);
        Assert.Equal("INVITATION_EXPIRED", (await Assert.ThrowsAsync<ProjectException>(() => service.ChangeAsync(data.Applicant, null, second.InvitationId, "accept"))).ErrorCode);
        db.ChangeTracker.Clear();
        Assert.Equal("expired", (await db.ProjectInvitations.SingleAsync(i => i.InvitationId == second.InvitationId)).Status);
    }

    [Fact]
    public async Task OptOut_BlocksSendAndAccept_ButDeclineAndCancelAllowArchivedContext()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await Send(leader, data);
        Assert.Equal(HttpStatusCode.OK, (await member.PatchAsJsonAsync("/api/me/profile", new { open_to_team_invitations = false })).StatusCode);
        await ProjectApplicationHttpTests.Problem(await member.PostAsync(MemberPath(id, "accept"), null), HttpStatusCode.Conflict, "CANDIDATE_UNAVAILABLE");
        await ProjectApplicationHttpTests.Problem(await leader.PostAsJsonAsync(LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant }), HttpStatusCode.NotFound, "CANDIDATE_NOT_FOUND");
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TindyDbContext>().Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "archived"));
        await RecruitmentHttp.Data(await member.PostAsync(MemberPath(id, "decline"), null));
        var other = await TeammateTestData.Seed(factory);
        using var otherLeader = RecruitmentHttp.Client(factory, other.Leader);
        var cancelId = await Send(otherLeader, other);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TindyDbContext>().Projects.Where(p => p.ProjectId == other.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "archived"));
        await RecruitmentHttp.Data(await otherLeader.GetAsync(LeaderPath(other)));
        await RecruitmentHttp.Data(await otherLeader.PostAsync($"{LeaderPath(other)}/{cancelId}/cancel", null));
    }

    [Theory]
    [InlineData("archived", "PROJECT_NOT_RECRUITING")]
    [InlineData("position", "POSITION_UNAVAILABLE")]
    [InlineData("capacity", "PROJECT_CAPACITY_EXCEEDED")]
    [InlineData("sender", "INVITATION_SENDER_UNAVAILABLE")]
    public async Task Accept_RechecksCurrentState_AndFailureLeavesPending(string change, string code)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await Send(leader, data);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            if (change == "archived") await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "archived"));
            if (change == "position") await db.ProjectPositions.Where(p => p.PositionId == data.Position).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "closed"));
            if (change == "capacity") await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.MaxTeamCapacity, 1));
            if (change == "sender") await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.LeaderId, data.Outsider));
        }
        await ProjectApplicationHttpTests.Problem(await member.PostAsync(MemberPath(id, "accept"), null), HttpStatusCode.Conflict, code);
        using var final = factory.Services.CreateScope(); var check = final.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal("pending", (await check.ProjectInvitations.SingleAsync(i => i.InvitationId == id)).Status);
        Assert.Equal(1, await check.ProjectMembers.CountAsync(m => m.ProjectId == data.Project));
    }

    [Fact]
    public async Task StrictBodies_Privacy_Ownership_HistoryDeletionGuard_AndBurstLimit()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var outsider = RecruitmentHttp.Client(factory, data.Outsider);
        foreach (var body in new[] { "{}", "[]", "{\"position_id\":null}", "{\"status\":\"accepted\"}", "{\"position_id\":\"x\",\"position_id\":\"y\"}" })
            await ProjectApplicationHttpTests.Problem(await leader.PostAsync(LeaderPath(data), new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await ProjectApplicationHttpTests.Problem(await outsider.GetAsync(LeaderPath(data)), HttpStatusCode.Forbidden, "PROJECT_ACCESS_DENIED");
        await ProjectApplicationHttpTests.Problem(await leader.PostAsJsonAsync(LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Leader }), HttpStatusCode.Conflict, "CANNOT_INVITE_SELF");
        var id = await Send(leader, data);
        foreach (var action in new[] { "accept", "decline" }) await ProjectApplicationHttpTests.Problem(await outsider.PostAsync(MemberPath(id, action), null), HttpStatusCode.NotFound, "INVITATION_NOT_FOUND");
        await ProjectApplicationHttpTests.Problem(await leader.DeleteAsync($"/api/projects/{data.Project}/positions/{data.Position}"), HttpStatusCode.Conflict, "POSITION_IN_USE");
        await RecruitmentHttp.Data(await leader.PostAsync($"{LeaderPath(data)}/{id}/cancel", null));
        await ProjectApplicationHttpTests.Problem(await leader.DeleteAsync($"/api/projects/{data.Project}/positions/{data.Position}"), HttpStatusCode.Conflict, "POSITION_IN_USE");
        // Fresh Leader has a fresh per-user rate-limit bucket; invalid attempts count.
        var quota = await TeammateTestData.Seed(factory); using var quotaLeader = RecruitmentHttp.Client(factory, quota.Leader);
        for (var i = 0; i < 120; i++) Assert.Equal(HttpStatusCode.BadRequest, (await quotaLeader.PostAsJsonAsync(LeaderPath(quota), new { })).StatusCode);
        await ProjectApplicationHttpTests.Problem(await quotaLeader.PostAsJsonAsync(LeaderPath(quota), new { }), HttpStatusCode.TooManyRequests, "INVITATION_RATE_LIMITED");
    }
}
