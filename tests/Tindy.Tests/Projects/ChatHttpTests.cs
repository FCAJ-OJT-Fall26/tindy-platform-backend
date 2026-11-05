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
    private static async Task<Guid> Create(HttpClient client, Guid target) =>
        (await RecruitmentHttp.Data(await client.PostAsJsonAsync("/api/chat/conversations/direct", new { user_id = target }))).GetProperty("conversation_id").GetGuid();
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
    [Fact]
    public async Task RestAndHubNotifyAllParticipantConnections_WithoutJoiningRoom_AndAfterReconnect()
    {
        var data = await TeammateTestData.Seed(factory);
        using var a = RecruitmentHttp.Client(factory, data.Leader); using var b = RecruitmentHttp.Client(factory, data.Applicant);
        using var other = RecruitmentHttp.Client(factory, data.Outsider);
        using var recipient = await Connect(b); using var secondTab = await Connect(b); using var stranger = await Connect(other);
        var room = await Create(a, data.Applicant);
        await Send(a, room);
        Assert.Contains("message.created", await Receive(recipient));
        Assert.Contains("message.created", await Receive(secondTab));
        using var sender = await Connect(a);
        var invocation = JsonSerializer.Serialize(new { type = 1, invocationId = "1", target = "SendMessage", arguments = new object[] { room, "Via hub", Guid.NewGuid() } }) + "\u001e";
        await sender.SendAsync(Encoding.UTF8.GetBytes(invocation), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Contains("Via hub", await Receive(recipient));
        recipient.Abort();
        await Send(a, room);
        using var reconnected = await Connect(b);
        await Send(a, room);
        Assert.Contains("message.created", await Receive(reconnected));
        var history = await RecruitmentHttp.Data(await b.GetAsync(Room(room) + "/messages"));
        Assert.Equal(4, history.GetProperty("items").GetArrayLength());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await stranger.ReceiveAsync(new byte[16384], timeout.Token); });
    }
}
