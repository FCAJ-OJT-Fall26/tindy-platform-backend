using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Amazon.S3;
using System.Net;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Storage;
using Tindy.Infrastructure.Persistence;
using UglyToad.PdfPig;

namespace Tindy.Infrastructure.Media;

public sealed record AiPdfScanState(Guid UploadId, string Status, string? ScanResult);

public sealed class AiPdfQuarantine(TindyDbContext db, IFileStorageService storage, IOptions<GuardDutyOptions> options)
{
    internal static readonly TimeSpan ScanWindow = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async Task<AiPdfScanState> UploadAsync(Guid ownerId, Stream input, CancellationToken ct)
    {
        RequireEnabled();
        var target = storage.CreateWriteTarget(ownerId, MediaStorageKind.AiInputPdf);
        if (target.StorageProvider != "s3" || target.StorageContainer != options.Value.BucketName)
            throw new AiException(AiFailure.Unavailable);

        var bytes = new byte[AiProfileLimits.MaxQuarantinePdfBytes + 1];
        var length = await input.ReadAtLeastAsync(bytes, bytes.Length, false, ct);
        if (length > AiProfileLimits.MaxQuarantinePdfBytes)
            throw new AiException(AiFailure.PayloadTooLarge);
        if (!bytes.AsSpan(0, length).StartsWith("%PDF-"u8))
            throw new AiException(AiFailure.DocumentRejected);

        var now = DateTime.UtcNow;
        var asset = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(), OwnerUserId = ownerId, Purpose = "ai_input",
            StorageProvider = target.StorageProvider, StorageContainer = target.StorageContainer, StorageKey = target.StorageKey,
            ContentType = "application/pdf", ScanProvider = "guardduty", ScanStatus = "pending",
            Status = "pending_upload", ReservedBytes = length, CreatedAt = now, UploadExpiresAt = now.AddMinutes(2)
        };
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {ownerId} FOR UPDATE", ct);
            var cutoff = now - Retention;
            var recent = await db.MediaAssets.CountAsync(a => a.OwnerUserId == ownerId &&
                a.Purpose == "ai_input" && a.CreatedAt > cutoff, ct);
            if (recent >= 5)
                throw new MediaRequestException(429, "AI_PDF_UPLOAD_LIMIT", "At most five PDF uploads can be kept per account.");
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // Keep the reservation for cleanup if the write succeeds but finalizing metadata fails.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await using var content = new MemoryStream(bytes, 0, length, writable: false);
        var stored = await storage.SaveAsync(new StorageSaveRequest(ownerId, MediaStorageKind.AiInputPdf, content, target), timeout.Token);
        if (string.IsNullOrWhiteSpace(stored.ObjectVersionId) || stored.ObjectVersionId == "null" ||
            string.IsNullOrWhiteSpace(stored.ObjectETag))
            throw new AiException(AiFailure.Unavailable);

        var finalizedAt = DateTime.UtcNow;
        var finalized = await db.MediaAssets.Where(a => a.MediaAssetId == asset.MediaAssetId &&
                a.Status == "pending_upload" && a.UploadExpiresAt > finalizedAt)
            .ExecuteUpdateAsync(update => update
                .SetProperty(a => a.ObjectVersionId, stored.ObjectVersionId)
                .SetProperty(a => a.ObjectEtag, stored.ObjectETag)
                .SetProperty(a => a.FileSizeBytes, stored.FileSizeBytes)
                .SetProperty(a => a.Status, "processing")
                .SetProperty(a => a.UpdatedAt, finalizedAt)
                .SetProperty(a => a.UploadExpiresAt, (DateTime?)null), timeout.Token);
        if (finalized != 1) throw new AiException(AiFailure.Unavailable);
        return new(asset.MediaAssetId, "scanning", null);
    }

    public async Task<AiPdfScanState?> GetAsync(Guid ownerId, Guid uploadId, CancellationToken ct)
    {
        RequireEnabled();
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.MediaAssetId == uploadId &&
            a.OwnerUserId == ownerId && a.Purpose == "ai_input", ct);
        if (asset is null) return null;
        var age = DateTime.UtcNow - asset.CreatedAt;
        var status = asset.Status switch
        {
            _ when age >= Retention => "expired",
            "active" when asset.ScanStatus == "clean" => "clean",
            "rejected" => "blocked",
            "pending_upload" or "processing" when age < ScanWindow => "scanning",
            _ => "expired"
        };
        return new(uploadId, status, status == "blocked" ? asset.ScanStatus : null);
    }

    public async Task<AiProfileInput> ReadCleanInputAsync(Guid ownerId, Guid uploadId, string? text, CancellationToken ct)
    {
        RequireEnabled();
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.MediaAssetId == uploadId &&
            a.OwnerUserId == ownerId && a.Purpose == "ai_input", ct)
            ?? throw new MediaRequestException(404, "AI_PDF_NOT_FOUND", "PDF upload not found.");
        if (asset.Status != "active" || asset.ScanStatus != "clean" || asset.ScanCompletedAt is null ||
            DateTime.UtcNow - asset.CreatedAt >= Retention || asset.ScanProvider != "guardduty" ||
            asset.ContentType != "application/pdf" || asset.StorageProvider != "s3" ||
            asset.StorageContainer != options.Value.BucketName || !storage.CanAccess(asset.StorageProvider, asset.StorageContainer) ||
            !asset.StorageKey.StartsWith($"ai-input/pdf/{ownerId:N}/", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(asset.ObjectVersionId) || asset.ObjectVersionId == "null" ||
            string.IsNullOrWhiteSpace(asset.ObjectEtag))
            throw new AiException(AiFailure.DocumentScanRequired);
        if (asset.FileSizeBytes is not (> 0 and <= AiProfileLimits.MaxQuarantinePdfBytes))
            throw new AiException(AiFailure.DocumentRejected);

        var bytes = new byte[checked((int)asset.FileSizeBytes.Value + 1)];
        int length;
        try
        {
            await using var handle = await storage.OpenReadAsync(asset.StorageKey, asset.ObjectVersionId, ct);
            if (handle.ObjectVersionId != asset.ObjectVersionId ||
                handle.ObjectETag?.Trim('"') != asset.ObjectEtag.Trim('"'))
                throw new AiException(AiFailure.DocumentScanRequired);
            length = await handle.Content.ReadAtLeastAsync(bytes, bytes.Length, false, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new AiException(AiFailure.DocumentScanRequired);
        }
        catch (FileNotFoundException)
        {
            throw new AiException(AiFailure.DocumentScanRequired);
        }
        if (length != asset.FileSizeBytes || DateTime.UtcNow - asset.CreatedAt >= Retention)
            throw new AiException(AiFailure.DocumentScanRequired);
        Array.Resize(ref bytes, length);
        ValidateDocument(bytes);
        ct.ThrowIfCancellationRequested();
        return new AiProfileInput(text, bytes) { PdfScanVerified = true };
    }

    private static void ValidateDocument(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new AiException(AiFailure.DocumentRejected);
        try
        {
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            if (document.IsEncrypted || document.NumberOfPages is < 1 or > AiProfileLimits.MaxQuarantinePdfPages)
                throw new AiException(AiFailure.DocumentRejected);
        }
        catch (Exception ex) when (ex is not (AiException or OutOfMemoryException or OperationCanceledException))
        {
            throw new AiException(AiFailure.DocumentRejected);
        }
    }

    private void RequireEnabled()
    {
        if (!options.Value.Enabled || !options.Value.AiPdfUploadsEnabled)
            throw new AiException(AiFailure.Unavailable);
    }
}
