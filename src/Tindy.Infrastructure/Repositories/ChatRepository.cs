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
        _db.Conversations.Include(x => x.Project).ThenInclude(x => x!.Members).Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Type == "direct" && x.Status == "active" && x.Participants.Count == 2 &&
                x.Participants.Any(p => p.UserId == firstUserId) && x.Participants.Any(p => p.UserId == secondUserId), ct);

    public Task<Conversation?> GetConversationForUserAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        _db.Conversations.Include(x => x.Project).ThenInclude(x => x!.Members).Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.Status == "active" && x.ConsentSource != null && x.Participants.Any(p => p.UserId == userId), ct);

    public async Task<IReadOnlyList<Conversation>> GetConversationsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Covers projects seeded/imported after the migration as well.
        var missing = await _db.ProjectMembers.Where(m => m.UserId == userId && m.Status == "active" &&
            !_db.Conversations.Any(c => c.ProjectId == m.ProjectId)).Select(m => m.ProjectId).ToArrayAsync(ct);
        foreach (var projectId in missing) await GetProjectConversationAsync(userId, projectId, ct);
        return await _db.Conversations.AsNoTracking().Include(x => x.Project).ThenInclude(x => x!.Members).Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .Where(x => x.Status == "active" && x.ConsentSource != null && x.Participants.Any(p => p.UserId == userId))
            .ToListAsync(ct);
    }

    public async Task AddConversationAsync(Conversation conversation, CancellationToken ct = default) => await _db.Conversations.AddAsync(conversation, ct);
    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default) => await _db.ChatMessages.AddAsync(message, ct);

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, Guid userId, DateTime? before, Guid? beforeMessageId, int limit, CancellationToken ct = default) =>
        await VisibleMessages(conversationId, userId).Where(x => x.DeletedAt == null && (!before.HasValue || (x.CreatedAt < before.Value || (beforeMessageId.HasValue && x.CreatedAt == before.Value && x.MessageId.CompareTo(beforeMessageId.Value) < 0))))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.MessageId).Take(limit + 1).ToListAsync(ct);

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAroundAsync(Guid conversationId, Guid userId, Guid aroundMessageId, int limit, CancellationToken ct = default)
    {
        var target = await VisibleMessages(conversationId, userId).FirstOrDefaultAsync(m => m.MessageId == aroundMessageId && m.DeletedAt == null, ct);
        if (target == null)
            return await GetMessagesAsync(conversationId, userId, null, null, limit, ct);

        int half = Math.Max(1, limit / 2);
        var newer = await VisibleMessages(conversationId, userId)
            .Where(m => m.DeletedAt == null && (m.CreatedAt > target.CreatedAt || (m.CreatedAt == target.CreatedAt && m.MessageId.CompareTo(target.MessageId) > 0)))
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.MessageId)
            .Take(half)
            .ToListAsync(ct);

        int olderLimit = limit - newer.Count + 1;
        var older = await VisibleMessages(conversationId, userId)
            .Where(m => m.DeletedAt == null && (m.CreatedAt < target.CreatedAt || (m.CreatedAt == target.CreatedAt && m.MessageId.CompareTo(target.MessageId) <= 0)))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.MessageId)
            .Take(olderLimit)
            .ToListAsync(ct);

        newer.Reverse();
        return newer.Concat(older).ToList();
    }

    public Task<int> GetUnreadCountAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        VisibleMessages(conversationId, userId).CountAsync(x => x.SenderId != userId && x.DeletedAt == null &&
            !_db.ConversationParticipants.Any(p => p.ConversationId == conversationId && p.UserId == userId && p.LastReadAt != null && (p.LastReadAt > x.CreatedAt || (p.LastReadAt == x.CreatedAt && (p.LastReadMessageId == null || p.LastReadMessageId.Value.CompareTo(x.MessageId) >= 0)))), ct);

    public async Task<ChatMessage?> GetFirstUnreadMessageAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        return await VisibleMessages(conversationId, userId)
            .Where(x => x.SenderId != userId && x.DeletedAt == null &&
                !_db.ConversationParticipants.Any(p => p.ConversationId == conversationId && p.UserId == userId && p.LastReadAt != null &&
                    (p.LastReadAt > x.CreatedAt || (p.LastReadAt == x.CreatedAt && (p.LastReadMessageId == null || p.LastReadMessageId.Value.CompareTo(x.MessageId) >= 0)))))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.MessageId).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, (int UnreadCount, DateTime? FirstUnreadAt, Guid? FirstUnreadMessageId)>> GetUnreadSummariesBatchAsync(
        Guid userId, IReadOnlyList<Conversation> conversations, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, (int UnreadCount, DateTime? FirstUnreadAt, Guid? FirstUnreadMessageId)>();
        if (conversations.Count == 0) return result;

        foreach (var c in conversations)
        {
            result[c.ConversationId] = (0, null, null);
        }

        var candidateIds = conversations.Select(c => c.ConversationId).ToArray();
        var sql = """
            WITH unread AS (
                SELECT 
                    m.conversation_id AS "ConversationId",
                    m.message_id AS "MessageId",
                    m.created_at AS "CreatedAt",
                    ROW_NUMBER() OVER (PARTITION BY m.conversation_id ORDER BY m.created_at ASC, m.message_id ASC) AS rn,
                    COUNT(*) OVER (PARTITION BY m.conversation_id) AS total_unread
                FROM chat_messages m
                JOIN conversation_participants p ON m.conversation_id = p.conversation_id AND p.user_id = {0}
                WHERE m.conversation_id = ANY({1})
                  AND m.sender_id <> {0}
                  AND m.deleted_at IS NULL
                  AND (p.left_at IS NULL OR m.created_at <= p.left_at)
                  AND (p.last_read_at IS NULL OR m.created_at > p.last_read_at OR (m.created_at = p.last_read_at AND (p.last_read_message_id IS NULL OR m.message_id > p.last_read_message_id)))
            )
            SELECT "ConversationId", "MessageId", "CreatedAt", total_unread::int AS "TotalUnread"
            FROM unread
            WHERE rn = 1
            """;

        var rows = await _db.Database.SqlQueryRaw<UnreadSummaryRow>(sql, userId, candidateIds).ToListAsync(ct);
        foreach (var row in rows)
        {
            result[row.ConversationId] = (row.TotalUnread, row.CreatedAt, row.MessageId);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, (bool IsBlocked, bool BlockedByMe)>> BlockStatesBatchAsync(Guid userId, IReadOnlyList<Guid> otherUserIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, (bool IsBlocked, bool BlockedByMe)>();
        if (otherUserIds.Count == 0) return result;

        var pairKeyMap = otherUserIds.Distinct().ToDictionary(otherId => ChatRequests.Pair(userId, otherId), otherId => otherId);
        var pairKeys = pairKeyMap.Keys.ToArray();

        var rows = await _db.ChatBlocks.AsNoTracking()
            .Where(x => pairKeys.Contains(x.PairKey))
            .Select(x => new { x.PairKey, x.BlockerId })
            .ToArrayAsync(ct);

        var grouped = rows.GroupBy(x => x.PairKey).ToDictionary(g => g.Key, g => g.Select(x => x.BlockerId).ToArray());
        foreach (var (pairKey, otherId) in pairKeyMap)
        {
            if (grouped.TryGetValue(pairKey, out var blockers))
            {
                result[otherId] = (blockers.Length > 0, blockers.Contains(userId));
            }
            else
            {
                result[otherId] = (false, false);
            }
        }
        return result;
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
        var key = ChatRequests.Pair(userId, targetId);
        var room = await _db.Conversations.Include(x => x.Project).ThenInclude(x => x!.Members).Include(x => x.Participants).ThenInclude(x => x.User).ThenInclude(x => x.Profile)
            .SingleOrDefaultAsync(x => x.DirectKey == key && x.Status == "active" && x.ConsentSource != null, ct);
        if (room == null) throw new Tindy.Application.Exceptions.ChatRequestException(403, "CHAT_REQUEST_REQUIRED", "Send a chat request before messaging this person.");
        return room;
    }
    public async Task<(bool IsBlocked, bool BlockedByMe)> BlockStateAsync(Guid userId, Guid otherId, CancellationToken ct)
    {
        var key = ChatRequests.Pair(userId, otherId);
        var rows = await _db.ChatBlocks.AsNoTracking().Where(x => x.PairKey == key).Select(x => x.BlockerId).ToArrayAsync(ct);
        return (rows.Length > 0, rows.Contains(userId));
    }

    public async Task<ChatMessage> SaveMessageAsync(Conversation conversation, ChatMessage message, CancellationToken ct)
    {
        if (conversation.Type == "project") return await SaveProjectMessageAsync(conversation, message, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var parties = conversation.Participants.Select(x => x.UserId).ToArray();
        var pairKey = ChatRequests.Pair(parties[0], parties[1]);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({pairKey}, 0))", ct);
        if (!await _db.Conversations.AnyAsync(x => x.ConversationId == conversation.ConversationId && x.ConsentSource != null, ct))
            throw new Tindy.Application.Exceptions.ChatRequestException(403, "CHAT_REQUEST_REQUIRED", "Chat has not been accepted.");
        if (await _db.ChatBlocks.AnyAsync(x => x.PairKey == pairKey, ct) || await _db.Users.CountAsync(x => parties.Contains(x.UserId) && x.Status == "active" && x.DeletedAt == null && x.IsOnboardingCompleted, ct) != 2)
            throw new Tindy.Application.Exceptions.ChatRequestException(403, "CHAT_CONTACT_UNAVAILABLE", "This contact is unavailable.");
        if (message.ClientMessageId.HasValue)
        {
            var key = $"{message.SenderId:N}:{message.ClientMessageId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            var existing = await FindClientMessageAsync(message.SenderId, message.ClientMessageId.Value, ct);
            if (existing != null)
            {
                if (existing.ConversationId != message.ConversationId || existing.Body != message.Body || existing.ContextInputKey != null) throw new Tindy.Application.Exceptions.ConflictException("Client message ID was already used.");
                await tx.CommitAsync(ct); return existing;
            }
        }
        _db.ChatMessages.Add(message); await _db.SaveChangesAsync(ct);
        await _db.Conversations.Where(c => c.ConversationId == conversation.ConversationId && (c.LastMessageAt == null || c.LastMessageAt < message.CreatedAt))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessageAt, message.CreatedAt).SetProperty(c => c.UpdatedAt, message.CreatedAt), ct);
        await tx.CommitAsync(ct); return message;
    }

    private IQueryable<ChatMessage> VisibleMessages(Guid conversationId, Guid userId) =>
        _db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId &&
            _db.ConversationParticipants.Any(p => p.ConversationId == conversationId && p.UserId == userId && (p.LeftAt == null || m.CreatedAt <= p.LeftAt)));

    public async Task<Conversation> GetProjectConversationAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        // Synchronizes older/test-created projects too, serialized with all membership writes.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(_db, projectId, ct);
        if (!await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId, ct))
            throw new Tindy.Application.Exceptions.NotFoundException("Project conversation not found.");
        await ProjectChatSync.SyncAsync(_db, projectId, DateTime.UtcNow, ct);
        await tx.CommitAsync(ct);
        var roomId = await _db.Conversations.Where(x => x.ProjectId == projectId).Select(x => x.ConversationId).SingleAsync(ct);
        return await GetConversationForUserAsync(roomId, userId, ct) ?? throw new Tindy.Application.Exceptions.NotFoundException("Conversation not found.");
    }

    public async Task<IReadOnlyList<Guid>> ChangeProjectMembershipAsync(Guid actorId, Guid projectId, Guid memberId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(_db, projectId, ct);
        var project = await _db.Projects.SingleAsync(x => x.ProjectId == projectId, ct);
        if (project.LeaderId != actorId && actorId != memberId)
            throw new Tindy.Application.Exceptions.ChatRequestException(403, "PROJECT_ACTION_FORBIDDEN", "Only the project leader can remove another member.");
        if (memberId == project.LeaderId)
            throw new Tindy.Application.Exceptions.ConflictException("The project leader cannot leave or be removed.");
        var member = await _db.ProjectMembers.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == memberId, ct)
            ?? throw new Tindy.Application.Exceptions.NotFoundException("Project member not found.");
        if (member.Status != "active") { await tx.CommitAsync(ct); return Array.Empty<Guid>(); }
        await ProjectChatSync.SyncAsync(_db, projectId, DateTime.UtcNow, ct);
        // Position accounting uses the same project/position order as recruitment.
        if (member.PositionId.HasValue)
        {
            var position = await _db.ProjectPositions.FromSqlInterpolated($"SELECT * FROM project_positions WHERE position_id = {member.PositionId.Value} AND project_id = {projectId} FOR UPDATE").SingleAsync(ct);
            position.FilledCount = Math.Max(0, position.FilledCount - 1);
            if (position.Status == "filled" && project.Status == "recruiting" && project.ArchivedAt == null) position.Status = "open";
            position.UpdatedAt = DateTime.UtcNow;
        }
        member.Status = actorId == memberId ? "left" : "removed";
        await _db.SaveChangesAsync(ct);
        await ProjectChatSync.SyncAsync(_db, projectId, DateTime.UtcNow, ct);
        var users = await _db.ConversationParticipants.Where(p => p.Conversation.ProjectId == projectId).Select(p => p.UserId).ToArrayAsync(ct);
        await tx.CommitAsync(ct);
        return users;
    }

    private async Task<ChatMessage> SaveProjectMessageAsync(Conversation conversation, ChatMessage message, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(_db, conversation.ProjectId!.Value, ct);
        if (!await _db.ProjectMembers.AnyAsync(m => m.ProjectId == conversation.ProjectId && m.UserId == message.SenderId && m.Status == "active", ct) ||
            !await _db.ConversationParticipants.AnyAsync(p => p.ConversationId == conversation.ConversationId && p.UserId == message.SenderId && p.LeftAt == null, ct) ||
            !await _db.Users.AnyAsync(u => u.UserId == message.SenderId && u.Status == "active" && u.DeletedAt == null && u.IsOnboardingCompleted, ct))
            throw new Tindy.Application.Exceptions.ChatRequestException(403, "PROJECT_CHAT_READ_ONLY", "You can only read messages sent before you left this project.");
        if (message.ClientMessageId.HasValue)
        {
            var key = $"{message.SenderId:N}:{message.ClientMessageId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            var existing = await FindClientMessageAsync(message.SenderId, message.ClientMessageId.Value, ct);
            if (existing != null)
            {
                if (existing.ConversationId != message.ConversationId || existing.Body != message.Body || existing.ContextInputKey != null)
                    throw new Tindy.Application.Exceptions.ConflictException("Client message ID was already used.");
                await tx.CommitAsync(ct); return existing;
            }
        }
        // Assign after acquiring the membership lock so departure is an exact visibility boundary.
        message.CreatedAt = DateTime.UtcNow;
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        await _db.Conversations.Where(x => x.ConversationId == conversation.ConversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastMessageAt, message.CreatedAt).SetProperty(x => x.UpdatedAt, message.CreatedAt), ct);
        await tx.CommitAsync(ct);
        return message;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

public sealed class UnreadSummaryRow
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalUnread { get; set; }
}
