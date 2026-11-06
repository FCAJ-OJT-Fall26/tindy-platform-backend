using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ProjectGroupChatHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private static string Group(Guid project) => $"/api/chat/projects/{project}";
    private static string Room(Guid room) => $"/api/chat/conversations/{room}";
    private static async Task<JsonElement> Send(HttpClient user, Guid room, string body, Guid? key = null) =>
        await RecruitmentHttp.Data(await user.PostAsJsonAsync(Room(room) + "/messages", new { body, client_message_id = key ?? Guid.NewGuid() }));
    private static async Task<Guid> Open(HttpClient user, Guid project) =>
        (await RecruitmentHttp.Data(await user.GetAsync(Group(project)))).GetProperty("conversation_id").GetGuid();
    private static async Task Join(HttpClient leader, HttpClient member, ApplicationCase data)
    {
        var application = await RecruitmentHttp.Data(await member.PostAsJsonAsync($"/api/projects/{data.Project}/applications", new { position_id = data.Position }));
        await RecruitmentHttp.Data(await leader.PostAsync($"/api/projects/{data.Project}/applications/{application.GetProperty("application_id").GetGuid()}/accept", null));
    }

    [Fact]
    public async Task ProjectCreation_CreatesLeaderGroupInSameTransaction()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        var request = ProjectServiceTests.CreateValidRequest();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var skill = await db.Skills.Select(s => s.SkillId).FirstAsync();
        request = request with { Positions = [request.Positions[0] with { RoleId = data.Role, Skills = [new(skill, true)] }] };
        var result = await RecruitmentHttp.Data(await leader.PostAsJsonAsync("/api/projects", request, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        var projectId = result.GetProperty("project_id").GetGuid();
        var room = await db.Conversations.Include(c => c.Participants).SingleAsync(c => c.ProjectId == projectId);
        Assert.Equal(data.Leader, Assert.Single(room.Participants).UserId);
        Assert.Equal("project", room.Type);
        await Send(leader, room.ConversationId, "Leader can start alone");
        await db.Projects.Where(p => p.ProjectId == projectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Renamed project"));
        Assert.Equal("Renamed project", (await RecruitmentHttp.Data(await leader.GetAsync(Group(projectId)))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task ConcurrentSendsAndDeparture_HistoryStopsAtCommittedCutoff()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var room = await Open(leader, data.Project); await Join(leader, member, data);
        var sends = Enumerable.Range(0, 12).Select(i => Send(leader, room, "Concurrent " + i)).ToArray();
        var departure = member.PostAsync(Group(data.Project) + "/leave", null);
        await Task.WhenAll(sends.Cast<Task>().Append(departure));
        await RecruitmentHttp.Data(await departure);
        var dto = await RecruitmentHttp.Data(await member.GetAsync(Group(data.Project)));
        var cutoff = dto.GetProperty("left_at").GetDateTime();
        var all = await RecruitmentHttp.Data(await leader.GetAsync(Room(room) + "/messages?limit=100"));
        var visible = await RecruitmentHttp.Data(await member.GetAsync(Room(room) + "/messages?limit=100"));
        var expected = all.GetProperty("items").EnumerateArray().Where(m => m.GetProperty("created_at").GetDateTime() <= cutoff).Select(m => m.GetProperty("message_id").GetGuid()).ToArray();
        Assert.Equal(expected, visible.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("message_id").GetGuid()));
    }

    [Fact]
    public async Task SingleRoom_FullHistoryForNewAndReturningMembers_CutoffForFormerMembers()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        using var member = RecruitmentHttp.Client(factory, data.Applicant);
        using var outsider = RecruitmentHttp.Client(factory, data.Outsider);
        var rooms = await Task.WhenAll(Open(leader, data.Project), Open(leader, data.Project));
        Assert.Equal(rooms[0], rooms[1]); var room = rooms[0];
        var old = await Send(leader, room, "Before joining");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(Group(data.Project))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(Room(room) + "/messages")).StatusCode);
        await Join(leader, member, data);
        Assert.Equal(room, await Open(member, data.Project));
        var joined = await RecruitmentHttp.Data(await member.GetAsync(Room(room) + "/messages"));
        Assert.Contains(joined.GetProperty("items").EnumerateArray(), m => m.GetProperty("message_id").GetGuid() == old.GetProperty("message_id").GetGuid());
        Assert.Contains(joined.GetProperty("items").EnumerateArray(), m => m.GetProperty("message_type").GetString() == "system");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync(Group(data.Project) + "/members/" + data.Leader)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsync(Group(data.Project) + "/leave", null)).StatusCode);
        await RecruitmentHttp.Data(await member.PostAsync(Group(data.Project) + "/leave", null));
        await RecruitmentHttp.Data(await member.PostAsync(Group(data.Project) + "/leave", null));
        var before = await RecruitmentHttp.Data(await member.GetAsync(Group(data.Project)));
        var cutoff = before.GetProperty("left_at").GetDateTime();
        var future = await Send(leader, room, "After departure secret");
        var after = await RecruitmentHttp.Data(await member.GetAsync(Group(data.Project)));
        Assert.False(after.GetProperty("can_send").GetBoolean());
        Assert.Equal(before.GetProperty("unread_count").GetInt32(), after.GetProperty("unread_count").GetInt32());
        Assert.Equal(before.GetProperty("last_message_at").GetString(), after.GetProperty("last_message_at").GetString());
        var history = await RecruitmentHttp.Data(await member.GetAsync(Room(room) + "/messages"));
        Assert.All(history.GetProperty("items").EnumerateArray(), m => Assert.True(m.GetProperty("created_at").GetDateTime() <= cutoff));
        Assert.DoesNotContain(history.GetProperty("items").EnumerateArray(), m => m.GetProperty("body").GetString() == "After departure secret");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(Room(room) + "/messages", new { body = "No longer allowed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(Room(room) + "/read", new { last_read_message_id = future.GetProperty("message_id").GetGuid() })).StatusCode);
        await Join(leader, member, data);
        Assert.True((await RecruitmentHttp.Data(await member.GetAsync(Group(data.Project)))).GetProperty("can_send").GetBoolean());
        var returned = await RecruitmentHttp.Data(await member.GetAsync(Room(room) + "/messages"));
        Assert.Contains(returned.GetProperty("items").EnumerateArray(), m => m.GetProperty("body").GetString() == "After departure secret");
        await Send(member, room, "Back again");
        await RecruitmentHttp.Data(await leader.DeleteAsync(Group(data.Project) + "/members/" + data.Applicant));
        Assert.False((await RecruitmentHttp.Data(await member.GetAsync(Group(data.Project)))).GetProperty("can_send").GetBoolean());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(1, await db.Conversations.CountAsync(c => c.ProjectId == data.Project));
        Assert.Equal(0, (await db.ProjectPositions.SingleAsync(p => p.PositionId == data.Position)).FilledCount);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("archived")]
    public async Task InvitationMembership_ArchivedAndCompletedRemainWritable_PersonalBlocksDoNotBlockGroup(string status)
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var room = await Open(leader, data.Project);
        var invite = await ProjectInvitationHttpTests.Send(leader, data);
        await RecruitmentHttp.Data(await member.PostAsync(ProjectInvitationHttpTests.MemberPath(invite, "accept"), null));
        Assert.Equal(room, await Open(member, data.Project));
        await RecruitmentHttp.Data(await member.PutAsync("/api/chat/blocks/" + data.Leader, null));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status).SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
        }
        var key = Guid.NewGuid();
        var replies = await Task.WhenAll(Send(member, room, "Still collaborating", key), Send(member, room, "Still collaborating", key));
        Assert.Equal(replies[0].GetProperty("message_id").GetGuid(), replies[1].GetProperty("message_id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync(Room(room) + "/messages", new { body = "Changed retry", client_message_id = key })).StatusCode);
        var inbox = await RecruitmentHttp.Data(await leader.GetAsync("/api/chat/conversations"));
        var group = inbox.EnumerateArray().Single(c => c.GetProperty("conversation_id").GetGuid() == room);
        Assert.Equal("project", group.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, group.GetProperty("other_participant").ValueKind);
        Assert.True(group.GetProperty("unread_count").GetInt32() > 0);
        await RecruitmentHttp.Data(await leader.PostAsJsonAsync(Room(room) + "/read", new { last_read_message_id = replies[0].GetProperty("message_id").GetGuid() }));
    }

    [Fact]
    public async Task ThreeMembers_HubDeliveryStopsOnRemoval_AndHubCannotSendAfterDeparture()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant); using var third = RecruitmentHttp.Client(factory, data.Outsider);
        var room = await Open(leader, data.Project);
        await Join(leader, member, data);
        var invite = await ProjectInvitationHttpTests.Send(leader, data, data.Outsider);
        await RecruitmentHttp.Data(await third.PostAsync(ProjectInvitationHttpTests.MemberPath(invite, "accept"), null));
        using var socket = await Connect(member); using var other = await Connect(third);
        await Send(leader, room, "Visible to all three");
        Assert.Contains("Visible to all three", await Event(socket, "message.created"));
        Assert.Contains("Visible to all three", await Event(other, "message.created"));
        await RecruitmentHttp.Data(await leader.DeleteAsync(Group(data.Project) + "/members/" + data.Applicant));
        await Event(socket, "chat.requests.changed");
        await Send(leader, room, "Only current members");
        Assert.Contains("Only current members", await Event(other, "message.created"));
        var invocation = JsonSerializer.Serialize(new { type = 1, invocationId = "deny", target = "SendMessage", arguments = new object[] { room, "Forbidden via hub", Guid.NewGuid() } }) + "\u001e";
        await socket.SendAsync(Encoding.UTF8.GetBytes(invocation), WebSocketMessageType.Text, true, CancellationToken.None);
        var completion = await Event(socket, "deny");
        Assert.Contains("error", completion); Assert.DoesNotContain("Only current members", completion);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await socket.ReceiveAsync(new byte[16384], timeout.Token));
    }

    private async Task<WebSocket> Connect(HttpClient user)
    {
        var ws = factory.Server.CreateWebSocketClient();
        var token = user.DefaultRequestHeaders.Authorization!.Parameter;
        var socket = await ws.ConnectAsync(new Uri("wss://localhost/hubs/chat?access_token=" + token), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, CancellationToken.None);
        await Event(socket, "{}");
        return socket;
    }
    private static async Task<string> Event(WebSocket socket, string target)
    {
        for (var i = 0; i < 20; i++)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var buffer = new byte[32768]; var result = await socket.ReceiveAsync(buffer, timeout.Token);
            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (text.Contains(target)) return text;
        }
        throw new Exception("Expected event " + target);
    }
}
