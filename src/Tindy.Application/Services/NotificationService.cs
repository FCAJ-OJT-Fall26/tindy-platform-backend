using Tindy.Application.DTOs.Notifications;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class NotificationService(INotificationRepository repository, TimeProvider clock) : INotificationService
{
    public Task<NotificationsPage> GetMineAsync(Guid userId, NotificationQuery query, CancellationToken ct = default)
    {
        if (query.Filter is not ("all" or "unread") || query.Page < 1 || query.PageSize != 20)
            throw new ProjectException(400, "VALIDATION_FAILED", "Filter must be all or unread, page must be positive and page_size must be 20.");
        return repository.GetMineAsync(userId, query, ct);
    }

    public async Task<NotificationUnreadCount> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
        new(await repository.CountUnreadAsync(userId, ct));

    public async Task<NotificationDto> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default) =>
        await repository.MarkReadAsync(userId, notificationId, clock.GetUtcNow().UtcDateTime, ct)
        ?? throw new ProjectException(404, "NOTIFICATION_NOT_FOUND", "Notification not found.");

    public async Task<NotificationsMarkedRead> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime;
        return new(await repository.MarkAllReadAsync(userId, cutoff, ct));
    }
}
