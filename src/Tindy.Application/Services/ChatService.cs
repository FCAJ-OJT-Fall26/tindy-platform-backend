using Tindy.Application.DTOs.Chat;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ChatService : IChatService
{
    private readonly IChatRepository _repository;
    public ChatService(IChatRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<ChatUserDto>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return Array.Empty<ChatUserDto>();
        var users = await _repository.SearchUsersAsync(currentUserId, query.Trim(), ct);
        return users.Select(ToUser).ToArray();
    }

    public async Task<IReadOnlyList<ChatConversationDto>> GetConversationsAsync(Guid userId, CancellationToken ct = default)
    {
        var conversations = await _repository.GetConversationsForUserAsync(userId, ct);
        return await Task.WhenAll(conversations.Select(async c =>
        {
            var other = c.Participants.First(p => p.UserId != userId).User;
            var unreadCount = await _repository.GetUnreadCountAsync(c.ConversationId, userId, ct);
            var firstUnreadAt = unreadCount > 0 ? await _repository.GetFirstUnreadAtAsync(c.ConversationId, userId, ct) : null;
            return new ChatConversationDto(c.ConversationId, ToParticipant(other), c.LastMessageAt, unreadCount, firstUnreadAt);
        }));
    }

    public async Task<ChatConversationDto> CreateDirectConversationAsync(Guid userId, CreateDirectConversationRequest request, CancellationToken ct = default)
    {
        if (request.UserId == userId) throw new ConflictException("You cannot start a conversation with yourself.");
        var target = await _repository.GetActiveUserAsync(request.UserId, ct) ?? throw new NotFoundException("User not found.");
        var existing = await _repository.FindDirectConversationAsync(userId, request.UserId, ct);
        if (existing is not null) return new ChatConversationDto(existing.ConversationId, ToParticipant(target), existing.LastMessageAt, 0, null);

        var now = DateTime.UtcNow;
        var conversation = new Conversation { ConversationId = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        conversation.Participants.Add(new ConversationParticipant { ConversationId = conversation.ConversationId, UserId = userId, JoinedAt = now });
        conversation.Participants.Add(new ConversationParticipant { ConversationId = conversation.ConversationId, UserId = target.UserId, JoinedAt = now });
        await _repository.AddConversationAsync(conversation, ct);
        await _repository.SaveChangesAsync(ct);
        return new ChatConversationDto(conversation.ConversationId, ToParticipant(target), null, 0, null);
    }

    public async Task<ChatMessagesPage> GetMessagesAsync(Guid userId, Guid conversationId, DateTime? before, int limit, CancellationToken ct = default)
    {
        await RequireConversation(userId, conversationId, ct);
        limit = Math.Clamp(limit, 1, 100);
        var rows = await _repository.GetMessagesAsync(conversationId, before, limit, ct);
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).Reverse().Select(ToMessage).ToArray();
        return new ChatMessagesPage(items, hasMore, items.Length == 0 ? null : items[0].CreatedAt);
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid userId, Guid conversationId, SendChatMessageRequest request, CancellationToken ct = default)
    {
        var conversation = await RequireConversation(userId, conversationId, ct);
        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body)) throw new BusinessRuleException("Message cannot be empty.");
        if (body.Length > 4000) throw new BusinessRuleException("Message cannot exceed 4000 characters.");
        if (request.ClientMessageId.HasValue)
        {
            var existing = await _repository.GetMessagesAsync(conversationId, null, 1000, ct);
            var duplicate = existing.FirstOrDefault(x => x.SenderId == userId && x.ClientMessageId == request.ClientMessageId);
            if (duplicate is not null) return ToMessage(duplicate);
        }
        var message = new ChatMessage { MessageId = Guid.NewGuid(), ConversationId = conversationId, SenderId = userId, Body = body, ClientMessageId = request.ClientMessageId, CreatedAt = DateTime.UtcNow };
        conversation.LastMessageAt = message.CreatedAt;
        conversation.UpdatedAt = message.CreatedAt;
        await _repository.AddMessageAsync(message, ct);
        await _repository.SaveChangesAsync(ct);
        return ToMessage(message);
    }

    public async Task MarkReadAsync(Guid userId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await RequireConversation(userId, conversationId, ct);
        var participant = conversation.Participants.First(p => p.UserId == userId);
        await _repository.MarkReadAsync(participant, ct);
        await _repository.SaveChangesAsync(ct);
    }

    private async Task<Conversation> RequireConversation(Guid userId, Guid conversationId, CancellationToken ct) =>
        await _repository.GetConversationForUserAsync(conversationId, userId, ct) ?? throw new NotFoundException("Conversation not found.");

    private static ChatUserDto ToUser(User user) => new(user.UserId, user.Profile?.DisplayName ?? user.Username ?? "User", user.Username, user.Profile?.AvatarUrl);
    private static ChatParticipantDto ToParticipant(User user) => new(user.UserId, user.Profile?.DisplayName ?? user.Username ?? "User", user.Username, user.Profile?.AvatarUrl);
    private static ChatMessageDto ToMessage(ChatMessage message) => new(message.MessageId, message.ConversationId, message.SenderId, message.Body, message.CreatedAt, message.ClientMessageId);
}
