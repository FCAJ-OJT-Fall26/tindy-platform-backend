namespace Tindy.Application.DTOs.Profile;

// Shared command for POST and full-replacement PUT.
public sealed record CertificateUpsertRequest(
    string Title,
    string Issuer,
    string? CredentialId,
    string? CredentialUrl,
    DateOnly? IssuedAt,
    DateOnly? ExpiresAt);

public sealed record CertificateResponse(
    Guid CertificateId,
    string Title,
    string Issuer,
    string? CredentialId,
    string? CredentialUrl,
    string? IssuedAt,
    string? ExpiresAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
