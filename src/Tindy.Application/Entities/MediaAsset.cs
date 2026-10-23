namespace Tindy.Application.Entities;

/// <summary>
/// Metadata only. Storage access and state transitions belong to later media services.
/// Never delete this record before its physical object has been cleaned up.
/// </summary>
public sealed class MediaAsset
{
    public Guid MediaAssetId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Purpose { get; set; } = null!; // avatar | cover | certificate

    public string StorageProvider { get; set; } = null!; // local | s3
    public string StorageContainer { get; set; } = null!;
    public string StorageKey { get; set; } = null!;
    public string? ObjectVersionId { get; set; }
    public string? ObjectEtag { get; set; }

    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public long ReservedBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    public string Status { get; set; } = "pending_upload";
    public string ScanStatus { get; set; } = "not_required";
    public string? ScanProvider { get; set; }
    public DateTime? ScanCompletedAt { get; set; }
    public DateTime? UploadExpiresAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeleteRequestedAt { get; set; }
    public int CleanupAttempts { get; set; }
    public DateTime? NextCleanupAt { get; set; }
    public string? LastCleanupError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User Owner { get; set; } = null!;
}
