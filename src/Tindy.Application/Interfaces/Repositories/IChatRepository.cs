using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IChatRepository
{
    Task<IReadOnlyList<User>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default);
    Task<Conversation?> GetConversationForUserAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetConversationsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<Conversation?> FindDirectConversationAsync(Guid firstUserId, Guid secondUserId, CancellationToken ct = default);
    Task<User?> GetActiveUserAsync(Guid userId, CancellationToken ct = default);
    Task AddConversationAsync(Conversation conversation, CancellationToken ct = default);
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<Conversation> GetProjectConversationAsync(Guid userId, Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ChangeProjectMembershipAsync(Guid actorId, Guid projectId, Guid memberId, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, Guid userId, DateTime? before, Guid? beforeMessageId, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAroundAsync(Guid conversationId, Guid userId, Guid aroundMessageId, int limit, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task<ChatMessage?> GetFirstUnreadMessageAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, (int UnreadCount, DateTime? FirstUnreadAt, Guid? FirstUnreadMessageId)>> GetUnreadSummariesBatchAsync(Guid userId, IReadOnlyList<Conversation> conversations, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, (bool IsBlocked, bool BlockedByMe)>> BlockStatesBatchAsync(Guid userId, IReadOnlyList<Guid> otherUserIds, CancellationToken ct = default);
    Task MarkReadAsync(ConversationParticipant participant, ChatMessage message, CancellationToken ct = default);
    Task<ChatMessage?> FindMessageAsync(Guid conversationId, Guid messageId, CancellationToken ct);
    Task<ChatMessage?> FindClientMessageAsync(Guid senderId, Guid clientMessageId, CancellationToken ct);
    Task<Conversation> GetOrCreateDirectAsync(Guid userId, Guid targetId, CancellationToken ct);
    Task<ChatMessage> SaveMessageAsync(Conversation conversation, ChatMessage message, CancellationToken ct);
    Task<(bool IsBlocked, bool BlockedByMe)> BlockStateAsync(Guid userId, Guid otherId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct = default);
}
