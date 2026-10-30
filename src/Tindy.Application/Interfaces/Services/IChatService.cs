using Tindy.Application.DTOs.Chat;

namespace Tindy.Application.Interfaces.Services;

public interface IChatService
{
    Task<IReadOnlyList<ChatUserDto>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default);
    Task<IReadOnlyList<ChatConversationDto>> GetConversationsAsync(Guid userId, CancellationToken ct = default);
    Task<ChatConversationDto> CreateDirectConversationAsync(Guid userId, CreateDirectConversationRequest request, CancellationToken ct = default);
    Task<ChatMessagesPage> GetMessagesAsync(Guid userId, Guid conversationId, DateTime? before, int limit, CancellationToken ct = default);
    Task<ChatMessageDto> SendMessageAsync(Guid userId, Guid conversationId, SendChatMessageRequest request, CancellationToken ct = default);
    Task MarkReadAsync(Guid userId, Guid conversationId, CancellationToken ct = default);
}
