using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface ICertificateAttachmentRepository
{
    Task<T> ExecuteLockedAsync<T>(Guid ownerId, Func<CancellationToken, Task<T>> operation, CancellationToken ct);
    Task<bool> IsEligibleAsync(Guid userId, CancellationToken ct);
    Task<bool> CertificateExistsAsync(Guid ownerId, Guid certificateId, CancellationToken ct);
    Task<bool> HasAttachmentAsync(Guid ownerId, Guid certificateId, CancellationToken ct);
    Task<List<UserCertificateAttachment>> ListAsync(Guid ownerId, Guid certificateId, bool includePrivate, CancellationToken ct);
    Task<UserCertificateAttachment?> FindAsync(Guid ownerId, Guid certificateId, Guid attachmentId, CancellationToken ct);
    Task<List<MediaAsset>> QuotaAssetsAsync(Guid ownerId, CancellationToken ct);
    void Add(UserCertificateAttachment attachment);
    void Remove(UserCertificateAttachment attachment);
    Task SaveAsync(CancellationToken ct);
}
