using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Media;

public enum ScanEventDisposition { Processed, Ignored, Retry, Invalid }

public interface IGuardDutyEventHandler
{
    Task<ScanEventDisposition> ProcessAsync(string body, CancellationToken ct);
}

public sealed class GuardDutyEventHandler(TindyDbContext db, IOptions<GuardDutyOptions> options,
    IOptions<MediaLifecycleOptions> lifecycle) : IGuardDutyEventHandler
{
    public async Task<ScanEventDisposition> ProcessAsync(string body, CancellationToken ct)
    {
        var result = GuardDutyScanEvent.Parse(body, options.Value);
        if (result is null) return ScanEventDisposition.Invalid;
        db.ChangeTracker.Clear();
        var candidate = await db.MediaAssets.AsNoTracking().Where(a => a.StorageProvider == "s3" &&
            a.StorageContainer == result.Bucket && a.StorageKey == result.Key)
            .Select(a => new { a.MediaAssetId, a.OwnerUserId }).SingleOrDefaultAsync(ct);
        if (candidate is null) return ScanEventDisposition.Ignored; // Already deleted or never owned by this application.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {candidate.OwnerUserId} FOR UPDATE", ct);
        var asset = await db.MediaAssets.SingleOrDefaultAsync(a => a.MediaAssetId == candidate.MediaAssetId, ct);
        if (asset is null || asset.Purpose != "certificate" || asset.ContentType != "application/pdf" || asset.ScanProvider != "guardduty" ||
            asset.Status is "pending_delete" or "rejected" or "active") return ScanEventDisposition.Ignored;
        if (asset.Status == "pending_upload") return ScanEventDisposition.Retry; // Event can arrive before PUT finalization commits.
        if (asset.Status != "processing" || string.IsNullOrWhiteSpace(asset.ObjectEtag) ||
            asset.ObjectEtag.Trim('"') != result.ETag.Trim('"') || NormalizeVersion(asset.ObjectVersionId) != NormalizeVersion(result.VersionId) ||
            result.Time < asset.CreatedAt.AddMinutes(-5)) return ScanEventDisposition.Ignored;
        if (!await db.UserCertificateAttachments.AnyAsync(a => a.MediaAssetId == asset.MediaAssetId && a.OwnerUserId == asset.OwnerUserId, ct))
            return ScanEventDisposition.Ignored;

        var verdict = result.Result switch
        {
            "NO_THREATS_FOUND" => MalwareScanResult.Clean,
            "THREATS_FOUND" => MalwareScanResult.ThreatDetected,
            "UNSUPPORTED" => MalwareScanResult.Unsupported,
            _ => MalwareScanResult.Failed
        };
        MediaScanTransitions.Apply(asset, verdict, DateTime.UtcNow, lifecycle.Value.RejectedRetentionHours);
        if (result.Result is "ACCESS_DENIED" or "FAILED")
        {
            // GuardDuty has returned a terminal failure; a new upload is required.
            asset.Status = "rejected";
            asset.ScanStatus = result.Result == "ACCESS_DENIED" ? "access_denied" : "failed";
            asset.ReservedBytes = 0;
            asset.NextCleanupAt = DateTime.UtcNow.AddHours(lifecycle.Value.RejectedRetentionHours);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ScanEventDisposition.Processed;
    }

    private static string? NormalizeVersion(string? version) => string.IsNullOrEmpty(version) || version == "null" ? null : version;
}
