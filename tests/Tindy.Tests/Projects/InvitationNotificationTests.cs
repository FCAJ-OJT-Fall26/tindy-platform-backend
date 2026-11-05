using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class InvitationNotificationTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Theory]
    [InlineData("accept", "invitation_accepted")]
    [InlineData("decline", "invitation_declined")]
    [InlineData("cancel", "invitation_cancelled")]
    public async Task SendAndTransition_CreateExactlyOneSafeEventEach_AndRetryDoesNotDuplicate(string action, string type)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await ProjectInvitationHttpTests.Send(leader, data);
        var sendRetry = await leader.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant });
        Assert.Equal(HttpStatusCode.Conflict, sendRetry.StatusCode);
        Task<HttpResponseMessage> Change() => action == "cancel"
            ? leader.PostAsync($"{ProjectInvitationHttpTests.LeaderPath(data)}/{id}/cancel", null)
            : member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, action), null);
        var responses = await Task.WhenAll(Change(), Change());
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, (await Change()).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var rows = await db.Notifications.Where(n => n.InvitationId == id).ToListAsync();
        Assert.Equal(2, rows.Count);
        var received = Assert.Single(rows, n => n.Type == "invitation_received");
        Assert.Equal(data.Applicant, received.RecipientUserId); Assert.Equal(data.Leader, received.ActorUserId);
        var response = Assert.Single(rows, n => n.Type == type);
        Assert.Equal(action == "cancel" ? data.Applicant : data.Leader, response.RecipientUserId);
        Assert.Equal(action == "cancel" ? data.Leader : data.Applicant, response.ActorUserId);
        foreach (var row in rows)
        {
            Assert.Null(row.ReadAt);
            Assert.Equal(new[] { "actor_display_name", "position_title", "project_name" }, row.Payload.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal("Same display name", row.Payload.GetProperty("actor_display_name").GetString());
            Assert.DoesNotContain("private-canary", row.Payload.GetRawText());
            Assert.DoesNotContain("email", row.Payload.GetRawText());
        }
        // Display payload is a snapshot, not a live projection of renamed entities.
        var originalName = received.Payload.GetProperty("project_name").GetString();
        await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Renamed Project"));
        Assert.Equal(originalName, (await db.Notifications.AsNoTracking().SingleAsync(n => n.NotificationId == received.NotificationId)).Payload.GetProperty("project_name").GetString());
        if (action == "accept")
        {
            var detail = await RecruitmentHttp.Data(await member.GetAsync($"/api/projects/{data.Project}"));
            var team = detail.GetProperty("team_members").EnumerateArray().ToArray();
            Assert.Equal(data.Position, Assert.Single(team, m => m.GetProperty("user_id").GetGuid() == data.Applicant).GetProperty("position_id").GetGuid());
            Assert.Equal(JsonValueKind.Null, Assert.Single(team, m => m.GetProperty("is_leader").GetBoolean()).GetProperty("position_id").ValueKind);
        }
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task ExpiredInvitation_DoesNotProduceResponseNotification(string action)
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var invitation = TeammateTestData.Invitation(data, created: DateTime.UtcNow.AddHours(-169)); db.ProjectInvitations.Add(invitation); await db.SaveChangesAsync();
        using var client = RecruitmentHttp.Client(factory, action == "cancel" ? data.Leader : data.Applicant);
        var path = action == "cancel" ? $"{ProjectInvitationHttpTests.LeaderPath(data)}/{invitation.InvitationId}/cancel" : ProjectInvitationHttpTests.MemberPath(invitation.InvitationId, action);
        await ProjectApplicationHttpTests.Problem(await client.PostAsync(path, null), HttpStatusCode.Conflict, "INVITATION_EXPIRED");
        Assert.False(await db.Notifications.AnyAsync(n => n.InvitationId == invitation.InvitationId));
        Assert.Equal("expired", await db.ProjectInvitations.Where(i => i.InvitationId == invitation.InvitationId).Select(i => i.Status).SingleAsync());
    }

    [Fact]
    public async Task FailedSendAndAccept_DoNotCreateEventsOrMembership()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await db.UserProfiles.Where(p => p.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(p => p.OpenToTeamInvitations, false));
        Assert.Equal(HttpStatusCode.NotFound, (await leader.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant })).StatusCode);
        Assert.False(await db.Notifications.AnyAsync(n => n.Invitation.ProjectId == data.Project));
        Assert.False(await db.ProjectInvitations.AnyAsync(i => i.ProjectId == data.Project));
        await db.UserProfiles.Where(p => p.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(p => p.OpenToTeamInvitations, true));
        var id = await ProjectInvitationHttpTests.Send(leader, data);
        await db.ProjectPositions.Where(p => p.PositionId == data.Position).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "closed"));
        await ProjectApplicationHttpTests.Problem(await member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, "accept"), null), HttpStatusCode.Conflict, "POSITION_UNAVAILABLE");
        Assert.Equal("invitation_received", (await db.Notifications.SingleAsync(n => n.InvitationId == id)).Type);
        Assert.Equal("pending", await db.ProjectInvitations.Where(i => i.InvitationId == id).Select(i => i.Status).SingleAsync());
        Assert.False(await db.ProjectMembers.AnyAsync(m => m.ProjectId == data.Project && m.UserId == data.Applicant));
    }

    [Theory]
    [InlineData("send")]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task NotificationInsertFailure_RollsBackWholeInvitationTransaction(string action)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = action == "send" ? Guid.Empty : await ProjectInvitationHttpTests.Send(leader, data);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var type = action switch { "send" => "invitation_received", "accept" => "invitation_accepted", "decline" => "invitation_declined", _ => "invitation_cancelled" };
#pragma warning disable EF1002 // DDL identifiers/values below are generated UUIDs and fixed test event names only.
        await db.Database.ExecuteSqlRawAsync($"CREATE FUNCTION fail_notification_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.type = '{type}' AND EXISTS(SELECT 1 FROM project_invitations WHERE invitation_id=NEW.invitation_id AND project_id='{data.Project}'::uuid) THEN RAISE EXCEPTION 'notification test rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_notification_{suffix} BEFORE INSERT ON notifications FOR EACH ROW EXECUTE FUNCTION fail_notification_{suffix}();");
        try
        {
            var response = action switch
            {
                "send" => await leader.PostAsJsonAsync(ProjectInvitationHttpTests.LeaderPath(data), new { position_id = data.Position, invited_user_id = data.Applicant }),
                "cancel" => await leader.PostAsync($"{ProjectInvitationHttpTests.LeaderPath(data)}/{id}/cancel", null),
                _ => await member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, action), null)
            };
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            if (action == "send") Assert.False(await db.ProjectInvitations.AnyAsync(i => i.ProjectId == data.Project));
            else Assert.Equal("pending", await db.ProjectInvitations.Where(i => i.InvitationId == id).Select(i => i.Status).SingleAsync());
            Assert.Equal(action == "send" ? 0 : 1, await db.Notifications.CountAsync(n => n.Invitation.ProjectId == data.Project));
            Assert.False(await db.ProjectMembers.AnyAsync(m => m.ProjectId == data.Project && m.UserId == data.Applicant));
            Assert.Equal(0, await db.ProjectPositions.Where(p => p.PositionId == data.Position).Select(p => p.FilledCount).SingleAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER fail_notification_{suffix} ON notifications; DROP FUNCTION fail_notification_{suffix}();"); }
#pragma warning restore EF1002
    }

    [Fact]
    public async Task ReinviteAfterDeclineCooldown_UsesNewInvitationAndIndependentEvent()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var first = await ProjectInvitationHttpTests.Send(leader, data);
        await RecruitmentHttp.Data(await member.PostAsync(ProjectInvitationHttpTests.MemberPath(first, "decline"), null));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var old = DateTime.UtcNow.AddDays(-8); var responded = old.AddHours(1);
        await db.ProjectInvitations.Where(i => i.InvitationId == first).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.CreatedAt, old).SetProperty(i => i.ExpiresAt, old.AddHours(168))
            .SetProperty(i => i.RespondedAt, responded).SetProperty(i => i.UpdatedAt, responded));
        var second = await ProjectInvitationHttpTests.Send(leader, data);
        Assert.NotEqual(first, second);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.InvitationId == first));
        Assert.Single(await db.Notifications.Where(n => n.InvitationId == second).ToListAsync());
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    public async Task ResponseAfterLeaderChange_NeverNotifiesFormerLeader(string action)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var id = await ProjectInvitationHttpTests.Send(leader, data);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.LeaderId, data.Outsider));
        var response = await member.PostAsync(ProjectInvitationHttpTests.MemberPath(id, action), null);
        if (action == "accept")
            await ProjectApplicationHttpTests.Problem(response, HttpStatusCode.Conflict, "INVITATION_SENDER_UNAVAILABLE");
        else
        {
            await RecruitmentHttp.Data(response);
            var notification = await db.Notifications.SingleAsync(n => n.InvitationId == id && n.Type == "invitation_declined");
            Assert.Equal(data.Outsider, notification.RecipientUserId);
        }
        Assert.False(await db.Notifications.AnyAsync(n => n.InvitationId == id && n.RecipientUserId == data.Leader));
        Assert.Equal(action == "accept" ? 1 : 2, await db.Notifications.CountAsync(n => n.InvitationId == id));
        using var former = RecruitmentHttp.Client(factory, data.Leader);
        Assert.Empty((await RecruitmentHttp.Data(await former.GetAsync("/api/me/notifications"))).GetProperty("items").EnumerateArray());
    }
}
