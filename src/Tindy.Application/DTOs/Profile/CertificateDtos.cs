namespace Tindy.Application.DTOs.Profile;

// Shared command for POST and full-replacement PUT.
public sealed record CertificateUpsertRequest(
    string Title,
    string Issuer,
    string? Description,
    string? CredentialUrl);

public sealed record CertificateResponse(
    Guid CertificateId,
    string Title,
    string Issuer,
    string? Description,
    string? CredentialUrl,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
