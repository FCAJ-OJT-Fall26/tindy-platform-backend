using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IChatService _chatService;

    public ChatHub(IChatService chatService) => _chatService = chatService;

    private Guid CurrentUserId =>
        Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
            ? id
            : throw new InvalidCredentialsException();

    public async Task JoinConversation(Guid conversationId)
    {
        await _chatService.GetMessagesAsync(CurrentUserId, conversationId, null, 1, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(conversationId), Context.ConnectionAborted);
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(conversationId), Context.ConnectionAborted);

    public async Task<ChatMessageDto> SendMessage(Guid conversationId, string body, Guid? clientMessageId = null)
    {
        var message = await _chatService.SendMessageAsync(CurrentUserId, conversationId, new SendChatMessageRequest(body, clientMessageId), Context.ConnectionAborted);
        await Clients.Group(GroupName(conversationId)).SendAsync("message.created", message, Context.ConnectionAborted);
        return message;
    }

    public async Task MarkRead(Guid conversationId)
    {
        await _chatService.MarkReadAsync(CurrentUserId, conversationId, Context.ConnectionAborted);
        await Clients.Group(GroupName(conversationId)).SendAsync("conversation.read", new { conversationId, userId = CurrentUserId }, Context.ConnectionAborted);
    }

    private static string GroupName(Guid conversationId) => $"chat:{conversationId:N}";
}
