using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Media;

public sealed class MediaLifecycleService(TindyDbContext db, IFileStorageService storage, IFileMalwareScanner scanner,
    IOptions<MediaLifecycleOptions> options, ILogger<MediaLifecycleService> logger)
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await RunCleanupCycleAsync(ct);
        await RunScanCycleAsync(ct);
    }

    public async Task RunScanCycleAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var retryBefore = now.AddMinutes(-1);
        var overdue = now.AddHours(-2);
        var overdueCount = await db.MediaAssets.CountAsync(a => a.Status == "processing" && a.CreatedAt < overdue, ct);
        if (overdueCount > 0) logger.LogWarning("Media processing exceeded two hours. Count: {Count}", overdueCount);

        var scanIds = await db.MediaAssets.AsNoTracking().Where(a => a.Status == "processing" && a.ScanProvider == "clamav" &&
            a.StorageProvider == "local" && a.Purpose == "certificate" && a.ContentType == "application/pdf" &&
            (a.ScanCompletedAt == null || a.ScanCompletedAt <= retryBefore))
            .OrderBy(a => a.ScanCompletedAt).ThenBy(a => a.CreatedAt).Select(a => a.MediaAssetId).Take(options.Value.BatchSize).ToListAsync(ct);
        foreach (var id in scanIds) await IsolateFailure(() => ScanLocalAsync(id, ct), id, ct);
    }

    public async Task RunCleanupCycleAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cleanupIds = await db.MediaAssets.AsNoTracking().Where(a =>
            (a.Status == "pending_upload" && a.UploadExpiresAt <= now) ||
            (a.Status == "pending_delete" && (a.NextCleanupAt == null || a.NextCleanupAt <= now)) ||
            (a.Status == "rejected" && (a.NextCleanupAt == null || a.NextCleanupAt <= now) &&
                (a.CleanupAttempts == 0 || a.NextCleanupAt != null || a.LastCleanupError != null)))
            .OrderBy(a => a.NextCleanupAt).ThenBy(a => a.CreatedAt).Select(a => a.MediaAssetId).Take(options.Value.BatchSize).ToListAsync(ct);
        foreach (var id in cleanupIds) await IsolateFailure(() => CleanupAsync(id, ct), id, ct);
    }

    public async Task ScanLocalAsync(Guid mediaId, CancellationToken ct)
    {
        await WithOwnerLock(mediaId, async asset =>
        {
            if (asset.Status != "processing" || asset.ScanProvider != "clamav" || asset.StorageProvider != "local" ||
                asset.Purpose != "certificate" || asset.ContentType != "application/pdf" || !storage.CanAccess(asset.StorageProvider, asset.StorageContainer)) return;
            if (asset.ScanCompletedAt > DateTime.UtcNow.AddMinutes(-1)) return;
            if (!await db.UserCertificateAttachments.AnyAsync(a => a.MediaAssetId == mediaId && a.OwnerUserId == asset.OwnerUserId, ct))
            {
                CertificateAttachmentService.MarkForDeletion(asset);
                await db.SaveChangesAsync(ct);
                return;
            }
            // The owner lock serializes scan results with delete/replace and other workers.
            // Bounded scanner timeout limits how long this transaction can block owner writes.
            MalwareScanResult result;
            try
            {
                await using var handle = await storage.OpenReadAsync(asset.StorageKey, asset.ObjectVersionId, ct);
                result = await scanner.ScanAsync(handle.Content, ct);
            }
            catch (FileNotFoundException) { result = MalwareScanResult.Failed; }
            MediaScanTransitions.Apply(asset, result, DateTime.UtcNow, options.Value.RejectedRetentionHours);
            if (result is MalwareScanResult.Failed or MalwareScanResult.Timeout or MalwareScanResult.Unavailable)
                logger.LogWarning("PDF scan remains blocked. MediaAssetId: {MediaAssetId}, Result: {Result}", mediaId, result);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task ReconcileMetadataAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var candidates = await db.MediaAssets.AsNoTracking().Where(a => (a.Status == "active" || a.Status == "processing") && a.CreatedAt <= cutoff &&
            !db.UserProfiles.Any(p => p.AvatarAssetId == a.MediaAssetId || p.CoverAssetId == a.MediaAssetId) &&
            !db.UserCertificateAttachments.Any(t => t.MediaAssetId == a.MediaAssetId))
            .OrderBy(a => a.CreatedAt).Select(a => a.MediaAssetId).Take(1000).ToListAsync(ct);
        foreach (var id in candidates)
        {
            await IsolateFailure(() => WithOwnerLock(id, async asset =>
            {
                if (asset.Status is not ("active" or "processing") || asset.CreatedAt > cutoff ||
                    await db.UserProfiles.AnyAsync(p => p.AvatarAssetId == id || p.CoverAssetId == id, ct) ||
                    await db.UserCertificateAttachments.AnyAsync(a => a.MediaAssetId == id, ct)) return;
                CertificateAttachmentService.MarkForDeletion(asset);
                await db.SaveChangesAsync(ct);
            }, ct), id, ct);
        }
    }

    public async Task CleanupAsync(Guid mediaId, CancellationToken ct)
    {
        await WithOwnerLock(mediaId, async asset =>
        {
            var now = DateTime.UtcNow;
            var expired = asset.Status == "pending_upload" && asset.UploadExpiresAt <= now;
            if (!expired && asset.Status is not ("pending_delete" or "rejected")) return;
            if (asset.NextCleanupAt > now) return;
            // Rejected files may be physically purged, but their linked metadata occupies
            // the certificate slot until the owner explicitly deletes the attachment.
            if (await db.UserProfiles.AnyAsync(p => p.AvatarAssetId == mediaId || p.CoverAssetId == mediaId, ct))
            {
                logger.LogWarning("Cleanup skipped referenced profile media. MediaAssetId: {MediaAssetId}", mediaId);
                return;
            }
            var linked = await db.UserCertificateAttachments.AnyAsync(a => a.MediaAssetId == mediaId, ct);
            if (asset.Status == "pending_delete" && linked)
            {
                logger.LogWarning("Cleanup skipped referenced certificate media. MediaAssetId: {MediaAssetId}", mediaId);
                return;
            }
            var retainRejectedAttachment = asset.Status == "rejected" && linked;
            // A rejected row with successful cleanup and no next attempt has no file left.
            if (retainRejectedAttachment && asset.CleanupAttempts > 0 && asset.NextCleanupAt is null && asset.LastCleanupError is null) return;
            if (expired)
            {
                var attachments = await db.UserCertificateAttachments.Where(a => a.MediaAssetId == mediaId).ToListAsync(ct);
                db.UserCertificateAttachments.RemoveRange(attachments);
                CertificateAttachmentService.MarkForDeletion(asset);
                // Persist unlink and quota release before any physical deletion.
                await db.SaveChangesAsync(ct);
            }
            asset.CleanupAttempts++;
            try
            {
                if (!storage.CanAccess(asset.StorageProvider, asset.StorageContainer))
                    throw new InvalidOperationException("Media storage location is not configured on this worker.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await storage.DeleteAsync(asset.StorageKey, asset.ObjectVersionId, timeout.Token);
                if (retainRejectedAttachment)
                {
                    asset.ReservedBytes = 0;
                    asset.NextCleanupAt = null;
                    asset.LastCleanupError = null;
                    asset.UpdatedAt = now;
                }
                else db.MediaAssets.Remove(asset); // Only after a confirmed, idempotent storage delete.
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                asset.Status = retainRejectedAttachment ? "rejected" : "pending_delete";
                asset.LastCleanupError = ex.GetType().Name[..Math.Min(100, ex.GetType().Name.Length)];
                asset.NextCleanupAt = now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(asset.CleanupAttempts, 7))));
                asset.UpdatedAt = now;
                db.Entry(asset).State = EntityState.Modified;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Media cleanup will retry. MediaAssetId: {MediaAssetId}, Attempt: {Attempt}, ErrorType: {ErrorType}",
                    mediaId, asset.CleanupAttempts, asset.LastCleanupError);
            }
        }, ct);
    }

    private async Task WithOwnerLock(Guid mediaId, Func<MediaAsset, Task> operation, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var owner = await db.MediaAssets.AsNoTracking().Where(a => a.MediaAssetId == mediaId).Select(a => (Guid?)a.OwnerUserId).SingleOrDefaultAsync(ct);
        if (!owner.HasValue) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {owner.Value} FOR UPDATE", ct);
        var asset = await db.MediaAssets.SingleOrDefaultAsync(a => a.MediaAssetId == mediaId, ct);
        if (asset is not null) await operation(asset);
        await tx.CommitAsync(ct);
    }

    private async Task IsolateFailure(Func<Task> operation, Guid mediaId, CancellationToken ct)
    {
        try { await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Media operation failed. MediaAssetId: {MediaAssetId}, ErrorType: {ErrorType}", mediaId, ex.GetType().Name);
        }
    }
}
