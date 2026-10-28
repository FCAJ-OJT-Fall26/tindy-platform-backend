using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Storage;

namespace Tindy.Infrastructure.Media;

public sealed class MediaOrphanSchedule
{
    public DateTime NextRunUtc { get; set; }
}

public sealed class MediaOrphanReconciler(TindyDbContext db, IFileStorageService storage, MediaOrphanSchedule schedule,
    MediaLifecycleService lifecycle, ILogger<MediaOrphanReconciler> logger)
{
    public async Task RunIfDueAsync(CancellationToken ct)
    {
        if (schedule.NextRunUtc > DateTime.UtcNow) return;
        await lifecycle.ReconcileMetadataAsync(ct);
        if (storage is not IStorageObjectInventory inventory)
        {
            schedule.NextRunUtc = DateTime.UtcNow.AddHours(24);
            return;
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Cross-instance exclusion; the local schedule repeats daily (and after a restart).
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({817340501L}) AS \"Value\"").SingleAsync(ct)) return;
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var location = storage.CreateWriteTarget(Guid.NewGuid(), Tindy.Application.Models.Storage.MediaStorageKind.CertificateImage);
        await foreach (var item in inventory.EnumerateAsync(ct))
        {
            if (item.LastModifiedUtc > cutoff) continue;
            // Any metadata protects the object: pending/active/terminal records have their own cleanup path.
            if (await db.MediaAssets.AsNoTracking().AnyAsync(a => a.StorageProvider == location.StorageProvider &&
                a.StorageContainer == location.StorageContainer && a.StorageKey == item.StorageKey, ct)) continue;
            try { await inventory.DeleteOrphanAsync(item, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Orphan cleanup failed. ErrorType: {ErrorType}", ex.GetType().Name);
            }
        }
        await tx.CommitAsync(ct);
        schedule.NextRunUtc = DateTime.UtcNow.AddHours(24);
    }
}
