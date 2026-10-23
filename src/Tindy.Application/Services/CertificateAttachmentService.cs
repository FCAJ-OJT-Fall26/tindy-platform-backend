using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;

namespace Tindy.Application.Services;

public sealed class CertificateAttachmentService(ICertificateAttachmentRepository repository, IFileStorageService storage,
    ICertificateFileProcessor processor) : ICertificateAttachmentService
{
    public const long MaxFileBytes = 10_000_000;
    public const long QuotaBytes = 200_000_000;

    public async Task<CertificateAttachmentResponse> UploadAsync(Guid ownerId, Guid certificateId, Stream content, string fileName, long length, CancellationToken ct)
    {
        if (length <= 0) throw Error(400, "MEDIA_EMPTY", "File cannot be empty.");
        if (length > MaxFileBytes) throw Error(413, "MEDIA_FILE_TOO_LARGE", "File exceeds the 10 MB limit.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var pdf = extension == ".pdf";
        if (!pdf && extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw Error(415, "MEDIA_UNSUPPORTED_TYPE", "Only PDF, JPEG, PNG and WebP files are supported.");
        var kind = pdf ? MediaStorageKind.CertificatePdf : MediaStorageKind.CertificateImage;
        var target = storage.CreateWriteTarget(ownerId, kind);
        var attachmentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await repository.ExecuteLockedAsync(ownerId, async token =>
        {
            await RequireCertificate(ownerId, certificateId, token);
            // Every linked row occupies the slot, including pending and rejected files.
            if (await repository.HasAttachmentAsync(ownerId, certificateId, token))
                throw Error(409, "MEDIA_ATTACHMENT_LIMIT", "Maximum one attachment per certificate. Delete the existing attachment before uploading another.");
            var assets = await repository.QuotaAssetsAsync(ownerId, token);
            if (assets.Count(a => a.Status == "pending_upload") >= 2)
                throw Error(429, "MEDIA_CONCURRENT_LIMIT", "Maximum two concurrent uploads per user.");
            RequireQuota(assets, length);
            repository.Add(new UserCertificateAttachment
            {
                AttachmentId = attachmentId, CertificateId = certificateId, OwnerUserId = ownerId, CreatedAt = now,
                MediaAsset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(), OwnerUserId = ownerId, Purpose = "certificate",
                    StorageProvider = target.StorageProvider, StorageContainer = target.StorageContainer, StorageKey = target.StorageKey,
                    OriginalFileName = SafeFileName(fileName), ContentType = pdf ? "application/pdf" : "image/webp",
                    ReservedBytes = length, Status = "pending_upload", ScanStatus = pdf ? "pending" : "not_required",
                    ScanProvider = pdf ? (target.StorageProvider == "s3" ? "guardduty" : "clamav") : null,
                    CreatedAt = now, UploadExpiresAt = now.AddMinutes(15)
                }
            });
            await repository.SaveAsync(token);
            return true;
        }, ct);

        try
        {
            // End writes before a reservation can expire, even if the caller never disconnects.
            using var uploadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            uploadTimeout.CancelAfter(TimeSpan.FromMinutes(14));
            var prepared = await processor.ProcessAsync(content, pdf, length, uploadTimeout.Token);
            if (prepared.Bytes.Length <= 0 || prepared.Bytes.Length > MaxFileBytes)
                throw Error(413, "MEDIA_FILE_TOO_LARGE", "Processed file exceeds the 10 MB limit.");
            await repository.ExecuteLockedAsync(ownerId, async token =>
            {
                var attachment = await RequirePending(ownerId, certificateId, attachmentId, token);
                var assets = await repository.QuotaAssetsAsync(ownerId, token);
                RequireQuota(assets.Where(a => a.MediaAssetId != attachment.MediaAssetId), prepared.Bytes.Length);
                attachment.MediaAsset.ReservedBytes = prepared.Bytes.Length;
                await repository.SaveAsync(token);
                return true;
            }, uploadTimeout.Token);

            await using var input = new MemoryStream(prepared.Bytes, writable: false);
            var stored = await storage.SaveAsync(new StorageSaveRequest(ownerId, kind, input, target), uploadTimeout.Token);
            return await repository.ExecuteLockedAsync(ownerId, async token =>
            {
                var attachment = await RequirePending(ownerId, certificateId, attachmentId, token);
                var asset = attachment.MediaAsset;
                asset.FileSizeBytes = stored.FileSizeBytes;
                asset.ObjectEtag = stored.ObjectETag;
                asset.ObjectVersionId = stored.ObjectVersionId;
                asset.Width = prepared.Width;
                asset.Height = prepared.Height;
                asset.Status = pdf ? "processing" : "active";
                asset.ReservedBytes = pdf ? stored.FileSizeBytes : 0;
                asset.ActivatedAt = pdf ? null : DateTime.UtcNow;
                asset.UploadExpiresAt = null;
                asset.UpdatedAt = DateTime.UtcNow;
                await repository.SaveAsync(token);
                return ToResponse(attachment);
            }, uploadTimeout.Token);
        }
        catch
        {
            // Keep durable metadata and exact target for the cleanup worker, including failed writes.
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await repository.ExecuteLockedAsync(ownerId, async token =>
                {
                    var attachment = await repository.FindAsync(ownerId, certificateId, attachmentId, token);
                    if (attachment?.MediaAsset.Status == "pending_upload")
                    {
                        MarkForDeletion(attachment.MediaAsset);
                        repository.Remove(attachment);
                        await repository.SaveAsync(token);
                    }
                    return true;
                }, cleanupTimeout.Token);
            }
            catch { /* Reservation expires after 15 minutes if persistence is unavailable. */ }
            throw;
        }
    }

    public async Task<IReadOnlyList<CertificateAttachmentResponse>> ListAsync(Guid callerId, Guid ownerId, Guid certificateId, CancellationToken ct)
    {
        if (!await repository.IsEligibleAsync(callerId, ct)) throw Missing();
        await RequireCertificate(ownerId, certificateId, ct);
        return (await repository.ListAsync(ownerId, certificateId, callerId == ownerId, ct)).Select(ToResponse).ToArray();
    }

    public Task<CertificateAttachmentResponse> ChangeVisibilityAsync(Guid ownerId, Guid certificateId, Guid attachmentId, string visibility, CancellationToken ct) =>
        repository.ExecuteLockedAsync(ownerId, async token =>
        {
            await RequireCertificate(ownerId, certificateId, token);
            var attachment = await repository.FindAsync(ownerId, certificateId, attachmentId, token) ?? throw Missing();
            if (visibility is not ("owner_only" or "members"))
                throw Error(400, "MEDIA_INVALID_VISIBILITY", "Visibility must be 'owner_only' or 'members'.");
            if (visibility == "members" && (attachment.MediaAsset.Status != "active" ||
                (attachment.MediaAsset.ContentType == "application/pdf" && attachment.MediaAsset.ScanStatus != "clean")))
                throw Error(409, "MEDIA_NOT_ACTIVE", "Only active, verified media can be shared.");
            attachment.Visibility = visibility;
            attachment.UpdatedAt = DateTime.UtcNow;
            await repository.SaveAsync(token);
            return ToResponse(attachment);
        }, ct);

    public async Task DeleteAsync(Guid ownerId, Guid certificateId, Guid attachmentId, CancellationToken ct)
    {
        await repository.ExecuteLockedAsync(ownerId, async token =>
        {
            await RequireCertificate(ownerId, certificateId, token);
            var attachment = await repository.FindAsync(ownerId, certificateId, attachmentId, token) ?? throw Missing();
            MarkForDeletion(attachment.MediaAsset);
            repository.Remove(attachment);
            await repository.SaveAsync(token);
            return true;
        }, ct);
    }

    public async Task<MediaQuotaResponse> GetQuotaAsync(Guid ownerId, CancellationToken ct)
    {
        if (!await repository.IsEligibleAsync(ownerId, ct)) throw Missing();
        var assets = await repository.QuotaAssetsAsync(ownerId, ct);
        var used = assets.Where(a => a.Status == "active").Sum(a => a.FileSizeBytes ?? 0);
        var reserved = assets.Where(a => a.Status is "pending_upload" or "processing").Sum(Charge);
        return new(QuotaBytes, used, reserved, Math.Max(0, QuotaBytes - used - reserved));
    }

    private async Task RequireCertificate(Guid ownerId, Guid certificateId, CancellationToken ct)
    {
        if (!await repository.IsEligibleAsync(ownerId, ct) || !await repository.CertificateExistsAsync(ownerId, certificateId, ct)) throw Missing();
    }

    private async Task<UserCertificateAttachment> RequirePending(Guid ownerId, Guid certificateId, Guid attachmentId, CancellationToken ct)
    {
        await RequireCertificate(ownerId, certificateId, ct);
        var attachment = await repository.FindAsync(ownerId, certificateId, attachmentId, ct) ?? throw Missing();
        if (attachment.MediaAsset.Status != "pending_upload" || attachment.MediaAsset.UploadExpiresAt <= DateTime.UtcNow) throw Missing();
        return attachment;
    }

    public static void MarkForDeletion(MediaAsset asset)
    {
        asset.Status = "pending_delete";
        asset.ReservedBytes = 0;
        asset.DeleteRequestedAt ??= DateTime.UtcNow;
        asset.NextCleanupAt = DateTime.UtcNow;
        asset.UpdatedAt = DateTime.UtcNow;
    }

    private static long Charge(MediaAsset a) => a.Status switch
    {
        "active" => a.FileSizeBytes ?? 0,
        "pending_upload" or "processing" => a.ReservedBytes > 0 ? a.ReservedBytes : a.FileSizeBytes ?? 0,
        _ => 0
    };

    private static void RequireQuota(IEnumerable<MediaAsset> assets, long additionalBytes)
    {
        if (assets.Sum(Charge) > QuotaBytes - additionalBytes)
            throw Error(409, "MEDIA_QUOTA_EXCEEDED", "Certificate storage quota is exceeded.");
    }

    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "attachment" : name[..Math.Min(name.Length, 255)];
    }

    private static CertificateAttachmentResponse ToResponse(UserCertificateAttachment a) => new(a.AttachmentId, a.MediaAssetId,
        a.MediaAsset.OriginalFileName ?? "attachment", a.MediaAsset.ContentType ?? "application/octet-stream",
        a.MediaAsset.FileSizeBytes ?? a.MediaAsset.ReservedBytes, a.MediaAsset.Status, a.Visibility, a.CreatedAt);

    private static MediaRequestException Missing() => Error(404, "MEDIA_NOT_FOUND", "Attachment or certificate not found.");
    private static MediaRequestException Error(int status, string code, string message) => new(status, code, message);
}
