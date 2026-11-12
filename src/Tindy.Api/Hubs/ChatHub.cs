using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
namespace Tindy.Api.Hubs;

public interface IChatHubRateLimiter
{
    bool TryAcquire(Guid userId);
}

public sealed class ChatHubRateLimiter : IChatHubRateLimiter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, List<long>> _users = new();

    public bool TryAcquire(Guid userId)
    {
        var now = Environment.TickCount64;
        var list = _users.GetOrAdd(userId, _ => new List<long>(6));
        lock (list)
        {
            list.RemoveAll(t => now - t > 2000);
            if (list.Count >= 5) return false;
            list.Add(now);
            return true;
        }
    }
}

[Authorize]
public sealed class ChatHub(IChatService chat, IChatHubRateLimiter rateLimiter) : Hub
{
    private Guid CurrentUserId => Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) && id != Guid.Empty ? id : throw new InvalidCredentialsException();
    public static string UserGroup(Guid userId) => $"chat-user:{userId:N}";
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(CurrentUserId));
        await base.OnConnectedAsync();
    }
    public Task<ChatMessageDto> SendMessage(Guid conversationId, string body, Guid? clientMessageId = null)
    {
        if (!rateLimiter.TryAcquire(CurrentUserId))
            throw new ChatRequestException(429, "CHAT_RATE_LIMIT", "You are sending messages too quickly. Please slow down.");
        return chat.SendMessageAsync(CurrentUserId, conversationId, new(body, clientMessageId), Context.ConnectionAborted);
    }
    public Task MarkRead(Guid conversationId, Guid lastReadMessageId) => chat.MarkReadAsync(CurrentUserId, conversationId, lastReadMessageId, Context.ConnectionAborted);
}