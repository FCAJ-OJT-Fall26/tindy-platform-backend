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

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, DateTime? before, Guid? beforeMessageId, int limit, CancellationToken ct = default) =>
        await _db.ChatMessages.AsNoTracking().Where(x => x.ConversationId == conversationId && x.DeletedAt == null && (!before.HasValue || (x.CreatedAt < before.Value || (beforeMessageId.HasValue && x.CreatedAt == before.Value && x.MessageId.CompareTo(beforeMessageId.Value) < 0))))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.MessageId).Take(limit + 1).ToListAsync(ct);

    public Task<int> GetUnreadCountAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        _db.ChatMessages.CountAsync(x => x.ConversationId == conversationId && x.SenderId != userId && x.DeletedAt == null &&
            !_db.ConversationParticipants.Any(p => p.ConversationId == conversationId && p.UserId == userId && p.LastReadAt != null && (p.LastReadAt > x.CreatedAt || (p.LastReadAt == x.CreatedAt && (p.LastReadMessageId == null || p.LastReadMessageId.Value.CompareTo(x.MessageId) >= 0)))), ct);

    public async Task<ChatMessage?> GetFirstUnreadMessageAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ChatMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.SenderId != userId && x.DeletedAt == null &&
                !_db.ConversationParticipants.Any(p => p.ConversationId == conversationId && p.UserId == userId && p.LastReadAt != null &&
                    (p.LastReadAt > x.CreatedAt || (p.LastReadAt == x.CreatedAt && (p.LastReadMessageId == null || p.LastReadMessageId.Value.CompareTo(x.MessageId) >= 0)))))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.MessageId).FirstOrDefaultAsync(ct);
    }
    public Task<ChatMessage?> FindMessageAsync(Guid conversationId, Guid messageId, CancellationToken ct) =>
        _db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.MessageId == messageId && x.DeletedAt == null, ct);
    public Task<ChatMessage?> FindClientMessageAsync(Guid senderId, Guid clientMessageId, CancellationToken ct) =>
        _db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(x => x.SenderId == senderId && x.ClientMessageId == clientMessageId, ct);
    public async Task MarkReadAsync(ConversationParticipant participant, ChatMessage message, CancellationToken ct = default)
    {
        await _db.ConversationParticipants.Where(p => p.ConversationId == participant.ConversationId && p.UserId == participant.UserId &&
            (p.LastReadAt == null || p.LastReadAt < message.CreatedAt || (p.LastReadAt == message.CreatedAt && p.LastReadMessageId != null && p.LastReadMessageId.Value.CompareTo(message.MessageId) < 0)))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastReadAt, message.CreatedAt).SetProperty(p => p.LastReadMessageId, message.MessageId), ct);
    }
    public async Task<Conversation> GetOrCreateDirectAsync(Guid userId, Guid targetId, CancellationToken ct)
    {
        var pair = new[] { userId.ToString("N"), targetId.ToString("N") }.OrderBy(x => x, StringComparer.Ordinal);
        var key = string.Join(":", pair);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        var existing = await FindDirectConversationAsync(userId, targetId, ct);
        if (existing != null) { await tx.CommitAsync(ct); return existing; }
        var now = DateTime.UtcNow;
        var conversation = new Conversation { ConversationId = Guid.NewGuid(), DirectKey = key, CreatedAt = now, UpdatedAt = now };
        conversation.Participants.Add(new() { UserId = userId, JoinedAt = now });
        conversation.Participants.Add(new() { UserId = targetId, JoinedAt = now });
        _db.Conversations.Add(conversation); await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return conversation;
    }
    public async Task<ChatMessage> SaveMessageAsync(Conversation conversation, ChatMessage message, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (message.ClientMessageId.HasValue)
        {
            var key = $"{message.SenderId:N}:{message.ClientMessageId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            var existing = await FindClientMessageAsync(message.SenderId, message.ClientMessageId.Value, ct);
            if (existing != null)
            {
                if (existing.ConversationId != message.ConversationId || existing.Body != message.Body) throw new Tindy.Application.Exceptions.ConflictException("Client message ID was already used.");
                await tx.CommitAsync(ct); return existing;
            }
        }
        _db.ChatMessages.Add(message); await _db.SaveChangesAsync(ct);
        await _db.Conversations.Where(c => c.ConversationId == conversation.ConversationId && (c.LastMessageAt == null || c.LastMessageAt < message.CreatedAt))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessageAt, message.CreatedAt).SetProperty(c => c.UpdatedAt, message.CreatedAt), ct);
        await tx.CommitAsync(ct); return message;
    }
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
