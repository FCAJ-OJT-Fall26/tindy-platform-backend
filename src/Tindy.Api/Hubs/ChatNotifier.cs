using Microsoft.AspNetCore.SignalR;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Interfaces.Services;
namespace Tindy.Api.Hubs;

public sealed class ChatNotifier(IHubContext<ChatHub> hub, ILogger<ChatNotifier> logger) : IChatNotifier
{
    public Task MessageCreatedAsync(IEnumerable<Guid> users, ChatMessageDto message, CancellationToken ct) => Publish(users, "message.created", message);
    public Task RequestsChangedAsync(IEnumerable<Guid> users, CancellationToken ct) => Publish(users, "chat.requests.changed", new { changed = true });
    public Task ReadAsync(IEnumerable<Guid> users, Guid conversationId, CancellationToken ct) => Publish(users, "conversation.read", new { conversationId });
    private async Task Publish(IEnumerable<Guid> users, string name, object payload)
    {
        try { await hub.Clients.Groups(users.Distinct().Select(ChatHub.UserGroup).ToArray()).SendAsync(name, payload); }
        catch (Exception ex) { logger.LogWarning(ex, "Chat delivery failed; clients resync on reconnect"); }
    }
}