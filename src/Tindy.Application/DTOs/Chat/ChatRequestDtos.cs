namespace Tindy.Application.DTOs.Chat;

public sealed record CreateChatRequest(Guid UserId, Guid ProjectId, Guid? PositionId, Guid? ApplicationId, string Body, Guid ClientRequestId);
public sealed record ChatRequestDto(Guid RequestId, Guid SenderId, Guid RecipientId, ChatUserDto OtherUser, Guid ProjectId, string ProjectName, Guid? PositionId, string? PositionTitle, Guid? ApplicationId, string Kind, string Body, string Status, string? EndReason, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RespondedAt, Guid? ConversationId);
public sealed record ChatRequestResult(ChatRequestDto? Request, Guid? ConversationId);
public sealed record ChatRequestsPage(IReadOnlyList<ChatRequestDto> Items, int PendingReceived, bool HasMore);
public sealed record ChatContextOption(Guid UserId, string DisplayName, Guid ProjectId, string ProjectName, Guid? PositionId, string? PositionTitle, Guid? ApplicationId);
public sealed record ChatBlockDto(Guid UserId, string DisplayName, DateTime CreatedAt);
public sealed record ShareChatContextRequest(Guid ProjectId, Guid? PositionId, string Body, Guid ClientMessageId);
