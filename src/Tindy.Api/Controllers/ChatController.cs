using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService) => _chatService = chatService;

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
            ? id
            : throw new InvalidCredentialsException();

    [HttpGet("users")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? q, CancellationToken ct) =>
        Ok(new { success = true, message = "Users retrieved successfully.", data = await _chatService.SearchUsersAsync(CurrentUserId, q ?? string.Empty, ct) });

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations(CancellationToken ct) =>
        Ok(new { success = true, message = "Conversations retrieved successfully.", data = await _chatService.GetConversationsAsync(CurrentUserId, ct) });

    [HttpPost("conversations/direct")]
    public async Task<IActionResult> CreateDirectConversation([FromBody] CreateDirectConversationRequest request, CancellationToken ct)
    {
        var data = await _chatService.CreateDirectConversationAsync(CurrentUserId, request, ct);
        return StatusCode(StatusCodes.Status201Created, new { success = true, message = "Conversation created successfully.", data });
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid conversationId, [FromQuery] DateTime? before, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(new { success = true, message = "Messages retrieved successfully.", data = await _chatService.GetMessagesAsync(CurrentUserId, conversationId, before, limit, ct) });

    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid conversationId, [FromBody] SendChatMessageRequest request, CancellationToken ct)
    {
        var data = await _chatService.SendMessageAsync(CurrentUserId, conversationId, request, ct);
        return StatusCode(StatusCodes.Status201Created, new { success = true, message = "Message sent successfully.", data });
    }

    [HttpPost("conversations/{conversationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid conversationId, CancellationToken ct)
    {
        await _chatService.MarkReadAsync(CurrentUserId, conversationId, ct);
        return Ok(new { success = true, message = "Conversation marked as read.", data = new { conversation_id = conversationId, read = true } });
    }
}
