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

public sealed class ChatHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private static string Room(Guid id) => $"/api/chat/conversations/{id}";
    private async Task<Guid> Create(HttpClient client, Guid target)
    {
        var options = await RecruitmentHttp.Data(await client.GetAsync("/api/chat/request-contexts?user_id=" + target));
        var option = options.EnumerateArray().First();
        var result = await RecruitmentHttp.Data(await client.PostAsJsonAsync("/api/chat/requests", new { user_id = target, project_id = option.GetProperty("project_id").GetGuid(), position_id = option.GetProperty("position_id").ValueKind == JsonValueKind.Null ? (Guid?)null : option.GetProperty("position_id").GetGuid(), body = "Opening", client_request_id = Guid.NewGuid() }));
        var request = result.GetProperty("request");
        using var recipient = RecruitmentHttp.Client(factory, request.GetProperty("recipient_id").GetGuid());
        var accepted = await RecruitmentHttp.Data(await recipient.PostAsync("/api/chat/requests/" + request.GetProperty("request_id").GetGuid() + "/accept", null));
        var room = accepted.GetProperty("conversation_id").GetGuid();
        // These regression tests isolate message delivery/pagination; request opening is covered separately.
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TindyDbContext>().ChatMessages.Where(m => m.ConversationId == room && m.Body == "Opening").ExecuteDeleteAsync();
        return room;
    }
    private static async Task<JsonElement> Send(HttpClient client, Guid room, Guid? key = null) =>
        await RecruitmentHttp.Data(await client.PostAsJsonAsync(Room(room) + "/messages", new { body = "Hello", client_message_id = key ?? Guid.NewGuid() }));
    [Fact]
    public async Task ConcurrentCreateAndRetry_KeepOneRoomAndOneMessage()
    {
        var data = await TeammateTestData.Seed(factory);
        using var a = RecruitmentHttp.Client(factory, data.Leader); using var b = RecruitmentHttp.Client(factory, data.Applicant);
        var rooms = await Task.WhenAll(Create(a, data.Applicant), Create(b, data.Leader));
        Assert.Equal(rooms[0], rooms[1]);
        var key = Guid.NewGuid();
        var sent = await Task.WhenAll(Send(a, rooms[0], key), Send(a, rooms[0], key));
        Assert.Equal(sent[0].GetProperty("message_id").GetGuid(), sent[1].GetProperty("message_id").GetGuid());
        var page = await RecruitmentHttp.Data(await a.GetAsync(Room(rooms[0]) + "/messages"));
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var conflict = await a.PostAsJsonAsync(Room(rooms[0]) + "/messages", new { body = "Changed", client_message_id = key });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }
    [Fact]
    public async Task EqualTimestampPaginationAndReadWatermark_AreStableAndScoped()
    {
        var data = await TeammateTestData.Seed(factory);
        using var a = RecruitmentHttp.Client(factory, data.Leader); using var b = RecruitmentHttp.Client(factory, data.Applicant);
        using var outsider = RecruitmentHttp.Client(factory, data.Outsider);
        var room = await Create(a, data.Applicant);
        var timestamp = DateTime.UtcNow.AddMinutes(-2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.ChatMessages.AddRange(Enumerable.Range(1, 125).Select(i => new ChatMessage { MessageId = Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"), ConversationId = room, SenderId = data.Leader, Body = "History", CreatedAt = timestamp }));
            await db.SaveChangesAsync();
        }
        var ids = new List<Guid>(); string query = "?limit=50";
        while (true)
        {
            var page = await RecruitmentHttp.Data(await b.GetAsync(Room(room) + "/messages" + query));
            ids.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("message_id").GetGuid()));
            if (!page.GetProperty("has_more").GetBoolean()) break;
            query = "?limit=50&before=" + Uri.EscapeDataString(page.GetProperty("next_before").GetString()!) + "&before_message_id=" + page.GetProperty("next_before_message_id").GetString();
        }
        Assert.Equal(125, ids.Distinct().Count()); Assert.Equal(125, ids.Count);
        var sorted = ids.OrderBy(x => x).ToArray();
        Assert.True((await b.PostAsJsonAsync(Room(room) + "/read", new { last_read_message_id = sorted[59] })).IsSuccessStatusCode);
        await Task.WhenAll(b.PostAsJsonAsync(Room(room) + "/read", new { last_read_message_id = sorted[9] }), b.PostAsJsonAsync(Room(room) + "/read", new { last_read_message_id = sorted[59] }));
        var inbox = await RecruitmentHttp.Data(await b.GetAsync("/api/chat/conversations"));
        Assert.Equal(sorted[60], inbox.EnumerateArray().Single(x => x.GetProperty("conversation_id").GetGuid() == room).GetProperty("first_unread_message_id").GetGuid());
        Assert.Equal(65, inbox.EnumerateArray().Single(x => x.GetProperty("conversation_id").GetGuid() == room).GetProperty("unread_count").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(Room(room) + "/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync(Room(room) + "/messages", new { body = "Forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync(Room(room) + "/read", new { last_read_message_id = Guid.NewGuid() })).StatusCode);
    }
    [Fact]
    public async Task HubCannotBypassConsentOrBlock()
    {
        var data = await TeammateTestData.Seed(factory);
        using var a = RecruitmentHttp.Client(factory, data.Leader); using var b = RecruitmentHttp.Client(factory, data.Applicant);
        var room = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.Conversations.Add(new Conversation { ConversationId = room, DirectKey = Tindy.Infrastructure.Repositories.ChatRequests.Pair(data.Leader, data.Applicant), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Participants = [new() { UserId = data.Leader, JoinedAt = DateTime.UtcNow }, new() { UserId = data.Applicant, JoinedAt = DateTime.UtcNow }] });
            await db.SaveChangesAsync();
        }
        using var socket = await Connect(a);
        async Task Denied(string id)
        {
            var payload = JsonSerializer.Serialize(new { type = 1, invocationId = id, target = "SendMessage", arguments = new object[] { room, "Must not be saved", Guid.NewGuid() } }) + "\u001e";
            await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);
            for (var i = 0; i < 20; i++) { var response = await Receive(socket); if (response.Contains("\"invocationId\":\"" + id + "\"")) { Assert.Contains("\"error\":", response); return; } }
            throw new Exception("Hub invocation was not completed");
        }
        await Denied("unaccepted");
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TindyDbContext>().Conversations.Where(x => x.ConversationId == room).ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsentSource, "legacy"));
        Assert.True((await b.PutAsync("/api/chat/blocks/" + data.Leader, null)).IsSuccessStatusCode);
        await Denied("blocked");
        using (var scope = factory.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<TindyDbContext>().ChatMessages.AnyAsync(x => x.ConversationId == room));
    }
    private async Task<WebSocket> Connect(HttpClient client)
    {
        var ws = factory.Server.CreateWebSocketClient();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter;
        var socket = await ws.ConnectAsync(new Uri("wss://localhost/hubs/chat?access_token=" + token), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, CancellationToken.None);
        await Receive(socket); return socket;
    }
    private static async Task<string> Receive(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16384];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
    private static async Task<string> ReceiveMessage(WebSocket socket)
    {
        for (var i = 0; i < 20; i++) { var payload = await Receive(socket); if (payload.Contains("message.created")) return payload; }
        throw new Exception("No message event received");
    }
    [Fact]
    public async Task RestAndHubNotifyAllParticipantConnections_WithoutJoiningRoom_AndAfterReconnect()
    {
        var data = await TeammateTestData.Seed(factory);
        using var a = RecruitmentHttp.Client(factory, data.Leader); using var b = RecruitmentHttp.Client(factory, data.Applicant);
        using var other = RecruitmentHttp.Client(factory, data.Outsider);
        var room = await Create(a, data.Applicant);
        using var recipient = await Connect(b); using var secondTab = await Connect(b); using var stranger = await Connect(other);
        await Send(a, room);
        Assert.Contains("message.created", await ReceiveMessage(recipient));
        Assert.Contains("message.created", await ReceiveMessage(secondTab));
        using var sender = await Connect(a);
        var invocation = JsonSerializer.Serialize(new { type = 1, invocationId = "1", target = "SendMessage", arguments = new object[] { room, "Via hub", Guid.NewGuid() } }) + "\u001e";
        await sender.SendAsync(Encoding.UTF8.GetBytes(invocation), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Contains("Via hub", await Receive(recipient));
        recipient.Abort();
        await Send(a, room);
        using var reconnected = await Connect(b);
        await Send(a, room);
        Assert.Contains("message.created", await ReceiveMessage(reconnected));
        var history = await RecruitmentHttp.Data(await b.GetAsync(Room(room) + "/messages"));
        Assert.Equal(4, history.GetProperty("items").GetArrayLength());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await stranger.ReceiveAsync(new byte[16384], timeout.Token); });
    }
}
