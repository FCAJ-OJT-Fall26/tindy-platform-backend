using Tindy.Application.DTOs.Profile;

namespace Tindy.Application.Interfaces.Services;

// HTTP Controllers must derive callerUserId from authenticated JWT sub, never the request body.
public interface ICertificateService
{
    Task<IReadOnlyList<CertificateResponse>> ListMyCertificatesAsync(Guid callerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CertificateResponse>> ListMemberCertificatesAsync(Guid targetUserId, CancellationToken cancellationToken = default);
    Task<CertificateResponse> CreateAsync(Guid callerUserId, CertificateUpsertRequest request, CancellationToken cancellationToken = default);
    Task<CertificateResponse> UpdateAsync(Guid callerUserId, Guid certificateId, CertificateUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid callerUserId, Guid certificateId, CancellationToken cancellationToken = default);
}
