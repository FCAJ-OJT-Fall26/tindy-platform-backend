using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Services;

/// <summary>
/// Uses verified external identities from the server-side OAuth middleware only.
/// Token is issued only after a single-use ticket is consumed and the User re-checked.
/// </summary>
public sealed class OAuthLoginFlowService : IOAuthLoginFlowService
{
    private readonly IExternalAuthService _externalAuth;
    private readonly IExternalAuthRepository _users;
    private readonly IOAuthLoginTicketStore _tickets;
    private readonly IJwtTokenService _jwt;

    public OAuthLoginFlowService(
        IExternalAuthService externalAuth, IExternalAuthRepository users,
        IOAuthLoginTicketStore tickets, IJwtTokenService jwt)
    {
        _externalAuth = externalAuth;
        _users = users;
        _tickets = tickets;
        _jwt = jwt;
    }

    public async Task<string> BeginTicketAsync(AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var signIn = await _externalAuth.ResolveSignInAsync(identity, cancellationToken);
        return await _tickets.IssueAsync(signIn.UserId, cancellationToken);
    }

    public async Task<OAuthExchangeResponse> ExchangeAsync(string ticket,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            throw new InvalidCredentialsException();

        // Consume first: invalid, expired or previously consumed ticket has no UserId.
        var userId = await _tickets.ConsumeAsync(ticket, cancellationToken);
        if (!userId.HasValue)
            throw new InvalidCredentialsException();

        // Re-query current account status/roles/onboarding (never trust ticket-stored roles).
        var user = await _users.GetUserByIdAsync(userId.Value, cancellationToken)
            ?? throw new InvalidCredentialsException();
        if (user.DeletedAt.HasValue ||
            !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase) ||
            user.Profile is null)
            throw new InvalidCredentialsException();

        var roles = user.UserRoles
            .Select(x => x.Role?.RoleName)
            .OfType<string>()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (roles.Length == 0)
            throw new InvalidCredentialsException();

        var jwt = _jwt.GenerateAccessToken(user.UserId, roles);
        return new OAuthExchangeResponse(jwt.AccessToken, jwt.TokenType, jwt.ExpiresIn,
            new OAuthExchangeUser(user.UserId, user.Profile.DisplayName,
                user.Profile.University ?? string.Empty,
                user.PrimaryEmail, user.Username, user.Status, roles,
                user.IsOnboardingCompleted),
            user.IsOnboardingCompleted ? "/discover" : "/onboarding");
    }
}
