namespace Tindy.Application.DTOs.Profile;

public sealed record CertificateAttachmentResponse(Guid AttachmentId, Guid MediaAssetId, string FileName, string ContentType,
    long FileSizeBytes, string Status, string Visibility, DateTime CreatedAt);

public sealed record MediaQuotaResponse(long QuotaBytes, long UsedBytes, long ReservedBytes, long AvailableBytes);

public sealed record AttachmentVisibilityRequest(string Visibility);
