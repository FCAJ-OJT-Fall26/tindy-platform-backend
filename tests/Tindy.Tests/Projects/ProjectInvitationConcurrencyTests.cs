using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

// Every action below enters a separate HTTP request scope/connection against
// disposable PostgreSQL, including the competing Application approvals.
public sealed class ProjectInvitationConcurrencyTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private static async Task<HttpResponseMessage[]> Race(params Func<Task<HttpResponseMessage>>[] actions)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(async action => { await gate.Task; return await action(); }).ToArray();
        gate.SetResult(); return await Task.WhenAll(tasks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendVersusApply_AndSendVersusSend_HaveExactlyOnePendingType(bool duplicateSend)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.ProjectInvitations.Add(TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-169)));
            await db.SaveChangesAsync();
        }
        var results = await Race(
            () => leader.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant }),
            () => duplicateSend
                ? leader.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(data), new { position_id = data.OtherPosition, invited_user_id = data.Applicant })
                : member.PostAsJsonAsync(ProjectApplicationHttpTests.Url(data.Project), new { position_id = data.OtherPosition }));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var final = factory.Services.CreateScope(); var check = final.ServiceProvider.GetRequiredService<TindyDbContext>();
        var pendingInvites = await check.ProjectInvitations.CountAsync(i => i.ProjectId == data.Project && i.Status == "pending");
        var pendingApplications = await check.ProjectApplications.CountAsync(a => a.ProjectId == data.Project && a.Status == "pending");
        Assert.Equal(1, pendingInvites + pendingApplications);
        Assert.Equal(pendingInvites, await check.Notifications.CountAsync(n => n.Invitation.ProjectId == data.Project && n.Type == "invitation_received"));
        Assert.Equal(1, await check.ProjectInvitations.CountAsync(i => i.ProjectId == data.Project && i.Status == "expired"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoAccepts_ForLastPositionOrProjectSlot_OnlyOneMembershipCommits(bool positionLimit)
    {
        var data = await TeammateTestData.Seed(factory, required: positionLimit ? 1 : 4, capacity: positionLimit ? 5 : 2);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        using var first = RecruitmentHttp.Client(factory, data.Applicant); using var second = RecruitmentHttp.Client(factory, data.Outsider);
        var a = await ProjectInvitationHttpTests.Send(leader, data);
        var b = await ProjectInvitationHttpTests.Send(leader, data, data.Outsider, positionLimit ? data.Position : data.OtherPosition);
        var responses = await Race(() => first.PostAsync(ProjectInvitationHttpTests.MemberPath(a, "accept"), null),
            () => second.PostAsync(ProjectInvitationHttpTests.MemberPath(b, "accept"), null));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(2, await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project && m.Status == "active"));
        Assert.Equal(1, await db.ProjectPositions.Where(p => p.ProjectId == data.Project).SumAsync(p => p.FilledCount));
        Assert.Equal(1, await db.ProjectInvitations.CountAsync(i => i.ProjectId == data.Project && i.Status == "accepted"));
        Assert.Equal(1, await db.ProjectInvitations.CountAsync(i => i.ProjectId == data.Project && i.Status == "pending"));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Invitation.ProjectId == data.Project && n.Type == "invitation_accepted"));
        if (positionLimit) Assert.Equal("filled", (await db.ProjectPositions.SingleAsync(p => p.PositionId == data.Position)).Status);
    }

    [Fact]
    public async Task InvitationAcceptVersusApplicationApproval_ShareProjectCapacityLock()
    {
        var data = await TeammateTestData.Seed(factory, required: 3, capacity: 2);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        using var member = RecruitmentHttp.Client(factory, data.Applicant); using var applicant = RecruitmentHttp.Client(factory, data.Outsider);
        var invitation = await ProjectInvitationHttpTests.Send(leader, data);
        var application = await ProjectApplicationHttpTests.Apply(applicant, data.Project, data.OtherPosition);
        var responses = await Race(() => member.PostAsync(ProjectInvitationHttpTests.MemberPath(invitation, "accept"), null),
            () => leader.PostAsync($"{ProjectApplicationHttpTests.Url(data.Project)}/{application}/accept", null));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(2, await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project && m.Status == "active"));
        Assert.Equal(1, await db.ProjectPositions.Where(p => p.ProjectId == data.Project).SumAsync(p => p.FilledCount));
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task AcceptVersusTerminalDecision_OnlyOneTransitionWins(string decision)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await ProjectInvitationHttpTests.Send(leader, data);
        var responses = await Race(() => member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, "accept"), null),
            () => decision == "cancel" ? leader.PostAsync($"{ProjectInvitationHttpTests.LeaderPath(data)}/{id}/cancel", null) : member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, decision), null));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var accepted = (await db.ProjectInvitations.SingleAsync(i => i.InvitationId == id)).Status == "accepted" ? 1 : 0;
        Assert.Equal(1 + accepted, await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project && m.Status == "active"));
        Assert.Equal(accepted, (await db.ProjectPositions.SingleAsync(p => p.PositionId == data.Position)).FilledCount);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.InvitationId == id));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.InvitationId == id && n.Type != "invitation_received"));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("close")]
    [InlineData("shrink")]
    public async Task ProjectAndPositionChangesVersusAccept_AreSerialized(string change)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await ProjectInvitationHttpTests.Send(leader, data);
        Task<HttpResponseMessage> Edit() => change switch
        {
            "archive" => leader.DeleteAsync($"/api/projects/{data.Project}"),
            "close" => leader.PutAsJsonAsync($"/api/projects/{data.Project}/positions/{data.Position}", new { status = "closed" }),
            _ => leader.PutAsJsonAsync($"/api/projects/{data.Project}", new { name = "Updated Project", description = "A sufficiently long project description.", category = "Web", difficulty_level = "intermediate", expected_commitment_hours = 10, duration = "3 months", max_team_capacity = 1, goals = Array.Empty<string>() })
        };
        var responses = await Race(() => member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, "accept"), null), Edit);
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest }));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var project = await db.Projects.SingleAsync(p => p.ProjectId == data.Project);
        var accepted = (await db.ProjectInvitations.SingleAsync(i => i.InvitationId == id)).Status == "accepted" ? 1 : 0;
        Assert.True(await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project && m.Status == "active") <= project.MaxTeamCapacity);
        Assert.Equal(accepted, (await db.ProjectPositions.SingleAsync(p => p.PositionId == data.Position)).FilledCount);
        if (change == "shrink") Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        // Archive/close after a committed accept may legitimately also succeed.
        else Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptOutVersusSendOrAccept_RespectsUserLockAndNeverFakesMembership(bool accept)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = accept ? await ProjectInvitationHttpTests.Send(leader, data) : Guid.Empty;
        var responses = await Race(() => member.PatchAsJsonAsync("/api/me/profile", new { open_to_team_invitations = false }),
            () => accept ? member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, "accept"), null)
                : leader.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant }));
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Contains(responses[1].StatusCode, accept ? new[] { HttpStatusCode.OK, HttpStatusCode.Conflict } : new[] { HttpStatusCode.Created, HttpStatusCode.NotFound });
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.False((await db.UserProfiles.SingleAsync(p => p.UserId == data.Applicant)).OpenToTeamInvitations);
        var joined = accept && responses[1].StatusCode == HttpStatusCode.OK ? 1 : 0;
        Assert.Equal(1 + joined, await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project && m.Status == "active"));
    }

    [Fact]
    public async Task ReciprocalLeaders_CanInviteEachOther_WithoutForeignKeyLockDeadlock()
    {
        var a = await TeammateTestData.Seed(factory); var b = await TeammateTestData.Seed(factory);
        using var leaderA = RecruitmentHttp.Client(factory, a.Leader); using var leaderB = RecruitmentHttp.Client(factory, b.Leader);
        var responses = await Race(() => leaderA.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(a), new { position_id = a.Position, invited_user_id = b.Leader }),
            () => leaderB.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(b), new { position_id = b.Position, invited_user_id = a.Leader }));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
    }

    [Fact]
    public async Task InjectedDatabaseFailure_RollsBackMembershipPositionAndInvitation()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await ProjectInvitationHttpTests.Send(leader, data);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var suffix = id.ToString("N");
#pragma warning disable EF1002 // Only test-generated UUIDs form DDL identifiers in a disposable database.
        await db.Database.ExecuteSqlRawAsync($"CREATE FUNCTION fail_invitation_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test rollback'; END $$; CREATE TRIGGER fail_invitation_{suffix} BEFORE UPDATE ON project_invitations FOR EACH ROW WHEN (NEW.invitation_id = '{id}'::uuid) EXECUTE FUNCTION fail_invitation_{suffix}();");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, "accept"), null)).StatusCode);
            Assert.Equal("pending", (await db.ProjectInvitations.SingleAsync(i => i.InvitationId == id)).Status);
            Assert.False(await db.ProjectMembers.AnyAsync(m => m.ProjectId == data.Project && m.UserId == data.Applicant));
            Assert.Equal(0, (await db.ProjectPositions.SingleAsync(p => p.PositionId == data.Position)).FilledCount);
            Assert.Equal("invitation_received", (await db.Notifications.SingleAsync(n => n.InvitationId == id)).Type);
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER fail_invitation_{suffix} ON project_invitations; DROP FUNCTION fail_invitation_{suffix}();"); }
#pragma warning restore EF1002
    }
}
