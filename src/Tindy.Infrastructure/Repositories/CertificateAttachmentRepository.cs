using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class CertificateAttachmentRepository(TindyDbContext db) : ICertificateAttachmentRepository
{
    public async Task<T> ExecuteLockedAsync<T>(Guid ownerId, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {ownerId} FOR UPDATE", ct);
        var result = await operation(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public Task<bool> IsEligibleAsync(Guid userId, CancellationToken ct) => db.Users.AsNoTracking().AnyAsync(u =>
        u.UserId == userId && u.Status == "active" && u.DeletedAt == null && u.IsOnboardingCompleted && u.Profile != null, ct);

    public Task<bool> CertificateExistsAsync(Guid ownerId, Guid certificateId, CancellationToken ct) =>
        db.UserCertificates.AsNoTracking().AnyAsync(c => c.UserId == ownerId && c.CertificateId == certificateId, ct);

    public Task<bool> HasAttachmentAsync(Guid ownerId, Guid certificateId, CancellationToken ct) =>
        db.UserCertificateAttachments.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerId && a.CertificateId == certificateId, ct);

    public Task<List<UserCertificateAttachment>> ListAsync(Guid ownerId, Guid certificateId, bool includePrivate, CancellationToken ct) =>
        db.UserCertificateAttachments.AsNoTracking().Include(a => a.MediaAsset)
            .Where(a => a.OwnerUserId == ownerId && a.CertificateId == certificateId && a.MediaAsset.Purpose == "certificate" &&
                (includePrivate || (a.Visibility == "members" && a.MediaAsset.Status == "active" &&
                    (a.MediaAsset.ContentType != "application/pdf" || a.MediaAsset.ScanStatus == "clean"))))
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.AttachmentId).ToListAsync(ct);

    public Task<UserCertificateAttachment?> FindAsync(Guid ownerId, Guid certificateId, Guid attachmentId, CancellationToken ct) =>
        db.UserCertificateAttachments.Include(a => a.MediaAsset).SingleOrDefaultAsync(a => a.OwnerUserId == ownerId &&
            a.CertificateId == certificateId && a.AttachmentId == attachmentId && a.MediaAsset.Purpose == "certificate", ct);

    public Task<List<MediaAsset>> QuotaAssetsAsync(Guid ownerId, CancellationToken ct) =>
        db.MediaAssets.AsNoTracking().Where(a => a.OwnerUserId == ownerId && a.Purpose == "certificate" &&
            (a.Status == "pending_upload" || a.Status == "processing" || a.Status == "active")).ToListAsync(ct);

    public void Add(UserCertificateAttachment attachment) => db.UserCertificateAttachments.Add(attachment);
    public void Remove(UserCertificateAttachment attachment) => db.UserCertificateAttachments.Remove(attachment);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_user_certificate_attachments_certificate_id" })
        {
            throw new MediaRequestException(409, "MEDIA_ATTACHMENT_LIMIT",
                "Maximum one attachment per certificate. Delete the existing attachment before uploading another.");
        }
    }
}
