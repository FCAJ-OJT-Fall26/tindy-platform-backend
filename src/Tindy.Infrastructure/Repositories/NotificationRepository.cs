using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Notifications;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class NotificationRepository(TindyDbContext db) : INotificationRepository
{
    private static readonly Expression<Func<Notification, NotificationDto>> Projection = n =>
        new(n.NotificationId, n.Type, n.ActorUserId, n.InvitationId, n.Payload, n.ReadAt != null, n.ReadAt, n.CreatedAt);

    public async Task<NotificationsPage> GetMineAsync(Guid userId, NotificationQuery query, CancellationToken ct)
    {
        var rows = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
        if (query.Filter == "unread") rows = rows.Where(n => n.ReadAt == null);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Skip((int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue)).Take(query.PageSize)
            .Select(Projection).ToListAsync(ct);
        var pages = (int)Math.Ceiling((double)total / query.PageSize);
        return new(items, new(query.Page, query.PageSize, total, pages, query.Page < pages, query.Page > 1));
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct) =>
        db.Notifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null, ct);

    public async Task<NotificationDto?> MarkReadAsync(Guid userId, Guid notificationId, DateTime now, CancellationToken ct)
    {
        // The NULL predicate is rechecked after a concurrent writer releases its row lock.
        // Only the first writer sets the timestamp; subsequent requests read that value.
        await db.Notifications.Where(n => n.RecipientUserId == userId && n.NotificationId == notificationId && n.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAt, now), ct);
        return await db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId && n.NotificationId == notificationId)
            .Select(Projection).SingleOrDefaultAsync(ct);
    }

    public Task<int> MarkAllReadAsync(Guid userId, DateTime cutoff, CancellationToken ct) =>
        // One UPDATE uses one PostgreSQL MVCC statement snapshot: later commits are
        // excluded even when their created_at predates cutoff. The application cutoff
        // also excludes events created after the service operation started.
        db.Notifications.Where(n => n.RecipientUserId == userId && n.ReadAt == null && n.CreatedAt <= cutoff)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAt, cutoff), ct);
}
