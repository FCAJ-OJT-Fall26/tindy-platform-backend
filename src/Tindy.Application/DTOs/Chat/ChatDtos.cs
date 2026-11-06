namespace Tindy.Application.DTOs.Chat;

public sealed record ChatUserDto(Guid UserId, string DisplayName, string? Username, string? AvatarUrl);
public sealed record CreateDirectConversationRequest(Guid UserId);
public sealed record MarkChatReadRequest(Guid LastReadMessageId);
public sealed record SendChatMessageRequest(string Body, Guid? ClientMessageId = null);
public sealed record ChatParticipantDto(Guid UserId, string DisplayName, string? Username, string? AvatarUrl);
public sealed record ChatConversationDto(Guid ConversationId, ChatParticipantDto? OtherParticipant, DateTime? LastMessageAt, int UnreadCount, DateTime? FirstUnreadAt, Guid? FirstUnreadMessageId, bool IsBlocked = false, bool BlockedByMe = false, string Type = "direct", Guid? ProjectId = null, string? Name = null, IReadOnlyList<ChatGroupMemberDto>? Participants = null, bool CanSend = true, DateTime? LeftAt = null);
public sealed record ChatGroupMemberDto(Guid UserId, string DisplayName, string? AvatarUrl, bool IsLeader, bool IsActive, DateTime? LeftAt);
public sealed record ChatMessageDto(Guid MessageId, Guid ConversationId, Guid SenderId, string Body, DateTime CreatedAt, Guid? ClientMessageId, Guid? ContextProjectId = null, string? ContextProjectName = null, string? ContextPositionTitle = null, string MessageType = "text");
public sealed record ChatMessagesPage(IReadOnlyList<ChatMessageDto> Items, bool HasMore, DateTime? NextBefore, Guid? NextBeforeMessageId);
