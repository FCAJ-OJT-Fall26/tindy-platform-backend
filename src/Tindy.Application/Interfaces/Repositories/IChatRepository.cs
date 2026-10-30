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
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, DateTime? before, int limit, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task<DateTime?> GetFirstUnreadAtAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task MarkReadAsync(ConversationParticipant participant, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
