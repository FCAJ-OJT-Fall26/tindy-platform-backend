namespace Tindy.Application.DTOs.Chat;

public sealed record ChatUserDto(Guid UserId, string DisplayName, string? Username, string? AvatarUrl);
public sealed record CreateDirectConversationRequest(Guid UserId);
public sealed record SendChatMessageRequest(string Body, Guid? ClientMessageId = null);
public sealed record ChatParticipantDto(Guid UserId, string DisplayName, string? Username, string? AvatarUrl);
public sealed record ChatConversationDto(Guid ConversationId, ChatParticipantDto OtherParticipant, DateTime? LastMessageAt, int UnreadCount, DateTime? FirstUnreadAt);
public sealed record ChatMessageDto(Guid MessageId, Guid ConversationId, Guid SenderId, string Body, DateTime CreatedAt, Guid? ClientMessageId);
public sealed record ChatMessagesPage(IReadOnlyList<ChatMessageDto> Items, bool HasMore, DateTime? NextBefore);
