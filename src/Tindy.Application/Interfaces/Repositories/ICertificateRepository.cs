using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface ICertificateRepository
{
    Task<bool> IsEligibleUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CertificateResponse>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CertificateResponse?> GetOwnedAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default);
    Task<UserCertificate?> FindOwnedForUpdateAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default);
    void Add(UserCertificate certificate);
    void Remove(UserCertificate certificate);
    Task DetachMediaAsync(Guid ownerUserId, Guid certificateId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Guid ownerUserId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
