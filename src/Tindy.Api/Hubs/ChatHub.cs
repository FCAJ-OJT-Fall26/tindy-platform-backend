using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
namespace Tindy.Api.Hubs;

[Authorize]
public sealed class ChatHub(IChatService chat) : Hub
{
    private Guid CurrentUserId => Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) && id != Guid.Empty ? id : throw new InvalidCredentialsException();
    public static string UserGroup(Guid userId) => $"chat-user:{userId:N}";
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(CurrentUserId));
        await base.OnConnectedAsync();
    }
    public Task<ChatMessageDto> SendMessage(Guid conversationId, string body, Guid? clientMessageId = null) => chat.SendMessageAsync(CurrentUserId, conversationId, new(body, clientMessageId), Context.ConnectionAborted);
    public Task MarkRead(Guid conversationId, Guid lastReadMessageId) => chat.MarkReadAsync(CurrentUserId, conversationId, lastReadMessageId, Context.ConnectionAborted);
}