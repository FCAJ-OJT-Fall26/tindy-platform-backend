using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Interfaces.Services;

public interface IExternalAuthService
{
    /// <summary>
    /// Resolves an already authenticated provider identity or creates an OAuth-only Tindy User.
    /// Does not issue JWTs or handle an OAuth authorization code.
    /// </summary>
    Task<ExternalSignInResult> ResolveSignInAsync(
        AuthenticatedExternalIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// STEP 05 will call this only after authenticating the Tindy User and validating
    /// the provider linking state/correlation against that exact UserId.
    /// This is not an HTTP endpoint.
    /// Returns true if a new link was created, false if the identical link already existed.
    /// </summary>
    Task<bool> LinkAsync(
        Guid currentUserId, AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default);
}
