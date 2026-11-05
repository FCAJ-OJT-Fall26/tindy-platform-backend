using System.Text.Json;
using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.DTOs.Notifications;

public sealed record NotificationQuery(string Filter = "all", int Page = 1, int PageSize = 20);
public sealed record NotificationDto(Guid NotificationId, string Type, Guid? ActorUserId, Guid InvitationId,
    JsonElement Payload, bool IsRead, DateTime? ReadAt, DateTime CreatedAt);
public sealed record NotificationsPage(IReadOnlyList<NotificationDto> Items, PaginationMeta Pagination);
public sealed record NotificationUnreadCount(int UnreadCount);
public sealed record NotificationsMarkedRead(int UpdatedCount);
