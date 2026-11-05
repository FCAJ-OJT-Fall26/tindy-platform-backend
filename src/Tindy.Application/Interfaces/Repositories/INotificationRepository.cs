using Tindy.Application.DTOs.Notifications;

namespace Tindy.Application.Interfaces.Repositories;

public interface INotificationRepository
{
    Task<NotificationsPage> GetMineAsync(Guid userId, NotificationQuery query, CancellationToken ct);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct);
    Task<NotificationDto?> MarkReadAsync(Guid userId, Guid notificationId, DateTime now, CancellationToken ct);
    Task<int> MarkAllReadAsync(Guid userId, DateTime cutoff, CancellationToken ct);
}
