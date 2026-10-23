using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class CertificateService : ICertificateService
{
    private readonly ICertificateRepository _repository;

    public CertificateService(ICertificateRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<CertificateResponse>> ListMyCertificatesAsync(Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireEligibleUserAsync(callerUserId, cancellationToken);
        return await _repository.ListOwnedAsync(callerUserId, cancellationToken);
    }

    // Member API caller is checked by the DefaultPolicy; target is checked here.
    public async Task<IReadOnlyList<CertificateResponse>> ListMemberCertificatesAsync(Guid targetUserId, CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty || !await _repository.IsEligibleUserAsync(targetUserId, cancellationToken))
            throw new NotFoundException("Profile was not found.");
        return await _repository.ListOwnedAsync(targetUserId, cancellationToken);
    }

    public Task<CertificateResponse> CreateAsync(Guid callerUserId, CertificateUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = CertificateInputValidator.Normalize(request);
        return _repository.ExecuteInTransactionAsync(callerUserId, async token =>
        {
            await RequireEligibleUserAsync(callerUserId, token);
            var certificate = new UserCertificate
            {
                CertificateId = Guid.NewGuid(),
                UserId = callerUserId,
                Title = normalized.Title,
                Issuer = normalized.Issuer,
                Description = normalized.Description,
                CredentialUrl = normalized.CredentialUrl,
                CreatedAt = DateTime.UtcNow
            };
            _repository.Add(certificate);
            await _repository.SaveChangesAsync(token);
            return await _repository.GetOwnedAsync(callerUserId, certificate.CertificateId, token)
                ?? throw new NotFoundException("Certificate was not found.");
        }, cancellationToken);
    }

    public Task<CertificateResponse> UpdateAsync(Guid callerUserId, Guid certificateId, CertificateUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = CertificateInputValidator.Normalize(request);
        return _repository.ExecuteInTransactionAsync(callerUserId, async token =>
        {
            await RequireEligibleUserAsync(callerUserId, token);
            var certificate = await _repository.FindOwnedForUpdateAsync(callerUserId, certificateId, token)
                ?? throw new NotFoundException("Certificate was not found.");
            certificate.Title = normalized.Title;
            certificate.Issuer = normalized.Issuer;
            certificate.Description = normalized.Description;
            certificate.CredentialUrl = normalized.CredentialUrl;
            certificate.UpdatedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync(token);
            return await _repository.GetOwnedAsync(callerUserId, certificateId, token)
                ?? throw new NotFoundException("Certificate was not found.");
        }, cancellationToken);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid certificateId, CancellationToken cancellationToken = default)
    {
        await _repository.ExecuteInTransactionAsync(callerUserId, async token =>
        {
            await RequireEligibleUserAsync(callerUserId, token);
            var certificate = await _repository.FindOwnedForUpdateAsync(callerUserId, certificateId, token)
                ?? throw new NotFoundException("Certificate was not found.");
            await _repository.DetachMediaAsync(callerUserId, certificateId, token);
            _repository.Remove(certificate);
            await _repository.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    private async Task RequireEligibleUserAsync(Guid userId, CancellationToken token)
    {
        if (userId == Guid.Empty || !await _repository.IsEligibleUserAsync(userId, token))
            throw new NotFoundException("Certificate was not found.");
    }
}
