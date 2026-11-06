using Tindy.Application.DTOs.Chat;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ChatService : IChatService
{
    private readonly IChatRepository _repository;
    private readonly IChatNotifier _notifier;
    public ChatService(IChatRepository repository, IChatNotifier notifier) { _repository = repository; _notifier = notifier; }

    public async Task<IReadOnlyList<ChatUserDto>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return Array.Empty<ChatUserDto>();
        var users = await _repository.SearchUsersAsync(currentUserId, query.Trim(), ct);
        return users.Select(ToUser).ToArray();
    }

    public async Task<IReadOnlyList<ChatConversationDto>> GetConversationsAsync(Guid userId, CancellationToken ct = default)
    {
        var conversations = await _repository.GetConversationsForUserAsync(userId, ct);
        var result = new List<ChatConversationDto>(conversations.Count);
        foreach (var c in conversations)
        {
            if (c.Type == "project") { result.Add(await ProjectDto(c, userId, ct)); continue; }
            var otherParticipant = c.Participants.FirstOrDefault(p => p.UserId != userId);
            if (otherParticipant?.User is null) continue;
            var unreadCount = await _repository.GetUnreadCountAsync(c.ConversationId, userId, ct);
            var firstUnreadAt = unreadCount > 0 ? await _repository.GetFirstUnreadMessageAsync(c.ConversationId, userId, ct) : null;
            var blocks = await _repository.BlockStateAsync(userId, otherParticipant.UserId, ct);
            result.Add(new ChatConversationDto(c.ConversationId, ToParticipant(otherParticipant.User), c.LastMessageAt, unreadCount, firstUnreadAt?.CreatedAt, firstUnreadAt?.MessageId, blocks.IsBlocked, blocks.BlockedByMe));
        }
        return result.OrderByDescending(c => c.LastMessageAt).ThenBy(c => c.ConversationId).ToArray();
    }

    public async Task<ChatConversationDto> CreateDirectConversationAsync(Guid userId, CreateDirectConversationRequest request, CancellationToken ct = default)
    {
        if (request.UserId == userId) throw new ConflictException("You cannot start a conversation with yourself.");
        var target = await _repository.GetActiveUserAsync(request.UserId, ct) ?? throw new NotFoundException("User not found.");
        var conversation = await _repository.GetOrCreateDirectAsync(userId, request.UserId, ct);
        var unread = await _repository.GetUnreadCountAsync(conversation.ConversationId, userId, ct);
        var first = unread > 0 ? await _repository.GetFirstUnreadMessageAsync(conversation.ConversationId, userId, ct) : null;
        return new ChatConversationDto(conversation.ConversationId, ToParticipant(target), conversation.LastMessageAt, unread, first?.CreatedAt, first?.MessageId);
    }

    public async Task<ChatMessagesPage> GetMessagesAsync(Guid userId, Guid conversationId, DateTime? before, Guid? beforeMessageId, int limit, CancellationToken ct = default)
    {
        await RequireConversation(userId, conversationId, ct);
        limit = Math.Clamp(limit, 1, 100);
        var rows = await _repository.GetMessagesAsync(conversationId, userId, before, beforeMessageId, limit, ct);
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).Reverse().Select(ToMessage).ToArray();
        return new ChatMessagesPage(items, hasMore, items.Length == 0 ? null : items[0].CreatedAt, items.Length == 0 ? null : items[0].MessageId);
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid userId, Guid conversationId, SendChatMessageRequest request, CancellationToken ct = default)
    {
        var conversation = await RequireConversation(userId, conversationId, ct);
        if (conversation.Type == "project" && conversation.Participants.Single(p => p.UserId == userId).LeftAt != null)
            throw new ChatRequestException(403, "PROJECT_CHAT_READ_ONLY", "You have left this project.");
        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body)) throw new BusinessRuleException("Message cannot be empty.");
        if (body.Length > 4000) throw new BusinessRuleException("Message cannot exceed 4000 characters.");
        if (request.ClientMessageId.HasValue)
        {
            var duplicate = await _repository.FindClientMessageAsync(userId, request.ClientMessageId.Value, ct);
            if (duplicate != null)
            {
                if (duplicate.ConversationId != conversationId || duplicate.Body != body || duplicate.ContextInputKey != null) throw new ConflictException("Client message ID was already used.");
                return ToMessage(duplicate);
            }
        }
        var message = new ChatMessage { MessageId = Guid.NewGuid(), ConversationId = conversationId, SenderId = userId, Body = body, ClientMessageId = request.ClientMessageId, CreatedAt = DateTime.UtcNow };
        message = await _repository.SaveMessageAsync(conversation, message, ct);
        var dto = ToMessage(message);
        await _notifier.MessageCreatedAsync(conversation.Participants.Select(p => p.UserId), dto, ct);
        return dto;
    }

    public async Task MarkReadAsync(Guid userId, Guid conversationId, Guid lastReadMessageId, CancellationToken ct = default)
    {
        var conversation = await RequireConversation(userId, conversationId, ct);
        var participant = conversation.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant != null)
        {
            var message = await _repository.FindMessageAsync(conversationId, lastReadMessageId, ct) ?? throw new NotFoundException("Message not found.");
            if (participant.LeftAt.HasValue && message.CreatedAt > participant.LeftAt.Value) throw new NotFoundException("Message not found.");
            await _repository.MarkReadAsync(participant, message, ct);
            await _notifier.ReadAsync(conversation.Participants.Select(p => p.UserId), conversationId, ct);
        }
    }

    public async Task<ChatConversationDto> GetProjectConversationAsync(Guid userId, Guid projectId, CancellationToken ct = default) =>
        await ProjectDto(await _repository.GetProjectConversationAsync(userId, projectId, ct), userId, ct);

    public async Task ChangeProjectMembershipAsync(Guid actorId, Guid projectId, Guid memberId, CancellationToken ct = default)
    {
        var users = await _repository.ChangeProjectMembershipAsync(actorId, projectId, memberId, ct);
        await _notifier.RequestsChangedAsync(users, ct);
    }

    private async Task<ChatConversationDto> ProjectDto(Conversation room, Guid userId, CancellationToken ct)
    {
        var self = room.Participants.Single(p => p.UserId == userId);
        var unread = await _repository.GetUnreadCountAsync(room.ConversationId, userId, ct);
        var first = unread > 0 ? await _repository.GetFirstUnreadMessageAsync(room.ConversationId, userId, ct) : null;
        var latest = await _repository.GetMessagesAsync(room.ConversationId, userId, null, null, 1, ct);
        var members = room.Participants.Where(p => self.LeftAt == null || p.JoinedAt <= self.LeftAt).Select(p =>
            new ChatGroupMemberDto(p.UserId, p.User.Profile?.DisplayName ?? p.User.Username ?? "Member", p.User.Profile?.AvatarUrl,
                p.UserId == room.Project!.LeaderId, p.LeftAt == null, p.LeftAt)).ToArray();
        var canSend = self.LeftAt == null && room.Project!.Members.Any(m => m.UserId == userId && m.Status == "active");
        return new ChatConversationDto(room.ConversationId, null, latest.FirstOrDefault()?.CreatedAt, unread, first?.CreatedAt, first?.MessageId,
            Type: "project", ProjectId: room.ProjectId, Name: room.Project!.Name, Participants: members, CanSend: canSend, LeftAt: self.LeftAt);
    }

    private async Task<Conversation> RequireConversation(Guid userId, Guid conversationId, CancellationToken ct) =>
        await _repository.GetConversationForUserAsync(conversationId, userId, ct) ?? throw new NotFoundException("Conversation not found.");

    private static ChatUserDto ToUser(User user) => new(user.UserId, user.Profile?.DisplayName ?? user.Username ?? "User", user.Username, user.Profile?.AvatarUrl);
    private static ChatParticipantDto ToParticipant(User user) => new(user.UserId, user.Profile?.DisplayName ?? user.Username ?? "User", user.Username, user.Profile?.AvatarUrl);
    private static ChatMessageDto ToMessage(ChatMessage message) => new(message.MessageId, message.ConversationId, message.SenderId, message.Body, message.CreatedAt, message.ClientMessageId, message.ContextProjectId, message.ContextProjectName, message.ContextPositionTitle, message.MessageType);
}
