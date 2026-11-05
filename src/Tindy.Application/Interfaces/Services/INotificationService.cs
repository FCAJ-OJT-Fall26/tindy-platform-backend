using Tindy.Application.DTOs.Notifications;

namespace Tindy.Application.Interfaces.Services;

public interface INotificationService
{
    Task<NotificationsPage> GetMineAsync(Guid userId, NotificationQuery query, CancellationToken ct = default);
    Task<NotificationUnreadCount> CountUnreadAsync(Guid userId, CancellationToken ct = default);
    Task<NotificationDto> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
    Task<NotificationsMarkedRead> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}
