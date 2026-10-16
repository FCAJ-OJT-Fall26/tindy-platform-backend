namespace Tindy.Application.Models.Authentication;

/// <summary>
/// Provider identity already verified by the OAuth middleware/adapter.
/// Never populate this object from arbitrary client JSON.
/// EmailVerified must be based on a provider-verified email signal, not just the presence of email.
/// </summary>
public sealed record AuthenticatedExternalIdentity(
    string Provider,
    string ProviderUserId,
    string? DisplayName,
    string? ProviderUsername,
    string? Email,
    bool EmailVerified);
