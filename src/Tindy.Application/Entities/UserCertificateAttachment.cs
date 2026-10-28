namespace Tindy.Application.Entities;

/// <summary>
/// Attachment relationship and visibility; physical-file metadata lives in MediaAsset.
/// OwnerUserId is repeated intentionally to enforce composite ownership FKs.
/// </summary>
public sealed class UserCertificateAttachment
{
    public Guid AttachmentId { get; set; }
    public Guid CertificateId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid MediaAssetId { get; set; }
    public string Visibility { get; set; } = "owner_only";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public UserCertificate Certificate { get; set; } = null!;
    public MediaAsset MediaAsset { get; set; } = null!;
}
