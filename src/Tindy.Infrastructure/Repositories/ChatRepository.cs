using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ChatRepository : IChatRepository
{
    private readonly TindyDbContext _db;
    public ChatRepository(TindyDbContext db) => _db = db;

    public async Task<IReadOnlyList<User>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default)
    {
        var normalizedQuery = query.ToLower();
        return await _db.Users.AsNoTracking().Include(x => x.Profile)
            .Where(x => x.UserId != currentUserId && x.Status == "active" && x.DeletedAt == null &&
                ((x.UsernameNormalized != null && x.UsernameNormalized.Contains(normalizedQuery)) ||
                 (x.PrimaryEmailNormalized != null && x.PrimaryEmailNormalized.Contains(normalizedQuery)) ||
                 (x.Profile != null && x.Profile.DisplayName.ToLower().Contains(normalizedQuery))))
            .OrderBy(x => x.Profile!.DisplayName).Take(20).ToListAsync(ct);
    }

    public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.Include(x => x.Profile).FirstOrDefaultAsync(x => x.UserId == userId && x.Status == "active" && x.DeletedAt == null, ct);

    public Task<Conversation?> FindDirectConversationAsync(Guid firstUserId, Guid secondUserId, CancellationToken ct = default) =>
        _db.Conversations.Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Type == "direct" && x.Status == "active" && x.Participants.Count == 2 &&
                x.Participants.Any(p => p.UserId == firstUserId) && x.Participants.Any(p => p.UserId == secondUserId), ct);

    public Task<Conversation?> GetConversationForUserAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        _db.Conversations.Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.Status == "active" && x.Participants.Any(p => p.UserId == userId), ct);

    public async Task<IReadOnlyList<Conversation>> GetConversationsForUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.Conversations.AsNoTracking().Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .Where(x => x.Status == "active" && x.Participants.Any(p => p.UserId == userId))
            .OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt).ToListAsync(ct);

    public async Task AddConversationAsync(Conversation conversation, CancellationToken ct = default) => await _db.Conversations.AddAsync(conversation, ct);
    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default) => await _db.ChatMessages.AddAsync(message, ct);

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, DateTime? before, int limit, CancellationToken ct = default) =>
        await _db.ChatMessages.AsNoTracking().Where(x => x.ConversationId == conversationId && x.DeletedAt == null && (!before.HasValue || x.CreatedAt < before.Value))
            .OrderByDescending(x => x.CreatedAt).Take(limit + 1).ToListAsync(ct);

    public Task<int> GetUnreadCountAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        _db.ChatMessages.CountAsync(x => x.ConversationId == conversationId && x.SenderId != userId && x.DeletedAt == null &&
            !_db.ConversationParticipants.Any(p => p.ConversationId == conversationId && p.UserId == userId && p.LastReadAt != null && p.LastReadAt >= x.CreatedAt), ct);

    public async Task<DateTime?> GetFirstUnreadAtAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        var participantReadAt = await _db.ConversationParticipants.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.UserId == userId)
            .Select(x => x.LastReadAt)
            .SingleOrDefaultAsync(ct);

        return await _db.ChatMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.SenderId != userId && x.DeletedAt == null &&
                (!participantReadAt.HasValue || x.CreatedAt > participantReadAt.Value))
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task MarkReadAsync(ConversationParticipant participant, CancellationToken ct = default) { participant.LastReadAt = DateTime.UtcNow; return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
