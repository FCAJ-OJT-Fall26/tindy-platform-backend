using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Infrastructure.Media;

internal static class MediaScanTransitions
{
    public static void Apply(MediaAsset asset, MalwareScanResult result, DateTime now, int rejectedRetentionHours)
    {
        asset.ScanCompletedAt = now;
        asset.UpdatedAt = now;
        asset.ScanStatus = result switch
        {
            MalwareScanResult.Clean => "clean",
            MalwareScanResult.ThreatDetected => "threat_detected",
            MalwareScanResult.Unsupported => "unsupported",
            _ => "failed"
        };
        if (result == MalwareScanResult.Clean)
        {
            asset.Status = "active";
            asset.ReservedBytes = 0;
            asset.ActivatedAt = now;
        }
        else if (result is MalwareScanResult.ThreatDetected or MalwareScanResult.Unsupported)
        {
            asset.Status = "rejected";
            asset.ReservedBytes = 0;
            asset.NextCleanupAt = now.AddHours(rejectedRetentionHours);
        }
        // Availability/timeout/failure stay processing and retain their reservation.
    }
}
