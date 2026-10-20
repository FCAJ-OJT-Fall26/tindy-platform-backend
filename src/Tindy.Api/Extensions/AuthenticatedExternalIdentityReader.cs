using System.Security.Claims;
using Tindy.Application.Constants;
using Tindy.Application.Models.Authentication;

namespace Tindy.Api.Extensions;

/// <summary>Only trust middleware-verified provider claims, never request body identity fields.</summary>
public static class AuthenticatedExternalIdentityReader
{
    public static AuthenticatedExternalIdentity? Read(ClaimsPrincipal? principal, string? provider)
    {
        if (principal is null || (provider != ExternalAuthProviders.Google &&
            provider != ExternalAuthProviders.GitHub)) return null;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id)) return null;
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var verified = false;
        if (provider == ExternalAuthProviders.Google)
        {
            verified = IsTrue(principal.FindFirstValue("tindy:google_verified_v2")) ||
                IsTrue(principal.FindFirstValue("tindy:google_verified_v3"));
        }
        else
        {
            var verifiedEmail = principal.FindFirstValue("tindy:github_verified_email");
            if (!string.IsNullOrWhiteSpace(verifiedEmail))
            {
                email = verifiedEmail;
                verified = true;
            }
        }
        return new AuthenticatedExternalIdentity(provider, id,
            principal.FindFirstValue(ClaimTypes.Name),
            provider == ExternalAuthProviders.GitHub ? principal.FindFirstValue(ClaimTypes.Name) : null,
            email, verified, OAuthAvatarUrl.Normalize(principal.FindFirstValue(
                provider == ExternalAuthProviders.Google ? "tindy:google_avatar_url" : "tindy:github_avatar_url")));
    }

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
