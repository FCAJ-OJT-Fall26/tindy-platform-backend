using Tindy.Application.DTOs.Chat;

namespace Tindy.Application.Interfaces.Services;

public interface IChatService
{
    Task<IReadOnlyList<ChatUserDto>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default);
    Task<IReadOnlyList<ChatConversationDto>> GetConversationsAsync(Guid userId, CancellationToken ct = default);
    Task<ChatConversationDto> CreateDirectConversationAsync(Guid userId, CreateDirectConversationRequest request, CancellationToken ct = default);
    Task<ChatMessagesPage> GetMessagesAsync(Guid userId, Guid conversationId, DateTime? before, Guid? beforeMessageId, int limit, CancellationToken ct = default);
    Task<ChatMessageDto> SendMessageAsync(Guid userId, Guid conversationId, SendChatMessageRequest request, CancellationToken ct = default);
    Task<ChatConversationDto> GetProjectConversationAsync(Guid userId, Guid projectId, CancellationToken ct = default);
    Task ChangeProjectMembershipAsync(Guid actorId, Guid projectId, Guid memberId, CancellationToken ct = default);
    Task MarkReadAsync(Guid userId, Guid conversationId, Guid lastReadMessageId, CancellationToken ct = default);
}
