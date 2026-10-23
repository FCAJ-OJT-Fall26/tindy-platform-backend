using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class CertificateRepository : ICertificateRepository
{
    private readonly TindyDbContext _context;

    public CertificateRepository(TindyDbContext context) => _context = context;

    public Task<bool> IsEligibleUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.Users.AsNoTracking().AnyAsync(user => user.UserId == userId &&
            user.Status == "active" && user.DeletedAt == null && user.IsOnboardingCompleted && user.Profile != null,
            cancellationToken);

    public async Task<IReadOnlyList<CertificateResponse>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await ForOwner(userId)
            .OrderByDescending(cert => cert.CreatedAt)
            .ThenBy(cert => cert.CertificateId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToResponse).ToArray();
    }

    public async Task<CertificateResponse?> GetOwnedAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default)
    {
        var row = await ForOwner(userId).SingleOrDefaultAsync(cert => cert.CertificateId == certificateId, cancellationToken);
        return row is null ? null : ToResponse(row);
    }

    public Task<UserCertificate?> FindOwnedForUpdateAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default) =>
        _context.UserCertificates.SingleOrDefaultAsync(cert => cert.UserId == userId && cert.CertificateId == certificateId,
            cancellationToken);

    public void Add(UserCertificate certificate) => _context.UserCertificates.Add(certificate);
    public void Remove(UserCertificate certificate) => _context.UserCertificates.Remove(certificate);
    public async Task DetachMediaAsync(Guid ownerUserId, Guid certificateId, CancellationToken cancellationToken = default)
    {
        var attachments = await _context.UserCertificateAttachments.Include(a => a.MediaAsset)
            .Where(a => a.OwnerUserId == ownerUserId && a.CertificateId == certificateId).ToListAsync(cancellationToken);
        foreach (var attachment in attachments) CertificateAttachmentService.MarkForDeletion(attachment.MediaAsset);
        _context.UserCertificateAttachments.RemoveRange(attachments);
    }
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Guid ownerUserId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // Consistent with Profile/Experience writes: lock the owner row before mutations.
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {ownerUserId} FOR UPDATE", cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<UserCertificate> ForOwner(Guid userId) =>
        _context.UserCertificates.AsNoTracking().Where(cert => cert.UserId == userId);

    private static CertificateResponse ToResponse(UserCertificate certificate) =>
        new(certificate.CertificateId, certificate.Title, certificate.Issuer,
            certificate.Description, certificate.CredentialUrl,
            certificate.CreatedAt, certificate.UpdatedAt);
}
