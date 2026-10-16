using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Api.Authorization;
using Tindy.Api.Extensions;
using Tindy.Application.Constants;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

/// <summary>STEP 05: explicit OAuth account linking. No JWT is included in redirect URLs.</summary>
[ApiController]
[Route("api/auth/connections")]
public sealed class OAuthConnectionsController : ControllerBase
{
    private const string TransactionItem = "tindy:link_transaction";
    private readonly IAccountConnectionsService _connections;
    private readonly IConfiguration _configuration;
    private readonly IAuthenticationSchemeProvider _schemes;

    public OAuthConnectionsController(IAccountConnectionsService connections,
        IConfiguration configuration, IAuthenticationSchemeProvider schemes)
    {
        _connections = connections;
        _configuration = configuration;
        _schemes = schemes;
    }

    [HttpGet]
    [Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        NoCache();
        var data = await _connections.GetAsync(CurrentUserId(), cancellationToken);
        return Ok(new { success = true, message = "Account connections retrieved successfully.", data });
    }

    [HttpPost("{provider}/start")]
    [Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
    [EnableRateLimiting("oauth-start")]
    public async Task<IActionResult> Start(string provider, CancellationToken cancellationToken)
    {
        NoCache();
        if (!Enabled()) return Unavailable();
        if (!TryProvider(provider, out var normalized)) return UnsupportedProvider();
        var startToken = await _connections.BeginAsync(CurrentUserId(), normalized, cancellationToken);
        var url = Url.ActionLink(nameof(LinkChallenge), "OAuthConnections",
            new { provider = normalized.ToLowerInvariant(), request = startToken });
        if (url is null) throw new InvalidOperationException("Cannot construct OAuth linking URL.");
        return Ok(new { success = true, message = "Account linking authorization is ready.", data = new { authorization_url = url } });
    }

    // Browser navigation cannot carry JWT Authorization header. This one-time
    // URL contains ONLY a short-lived opaque start token, NEVER the JWT.
    [HttpGet("{provider}/challenge")]
    [AllowAnonymous]
    [EnableRateLimiting("oauth-start")]
    public async Task<IActionResult> LinkChallenge(string provider, [FromQuery] string request,
        CancellationToken cancellationToken)
    {
        NoCache();
        if (!Enabled()) return Unavailable();
        if (!TryProvider(provider, out var normalized)) return UnsupportedProvider();
        if (await _schemes.GetSchemeAsync(normalized) is null) return Unavailable();
        var transactionId = await _connections.BeginChallengeAsync(request, normalized, cancellationToken);
        if (!transactionId.HasValue) return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: "Link request expired or already used.",
            extensions: new Dictionary<string, object?> { ["error_code"] = "OAUTH_LINK_REQUEST_INVALID" });
        await HttpContext.SignOutAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.ActionLink(nameof(LinkComplete), "OAuthConnections")
                ?? "/api/auth/connections/complete"
        };
        properties.Items["tindy:provider"] = normalized;
        properties.Items[TransactionItem] = transactionId.Value.ToString("D");
        return Challenge(properties, normalized);
    }

    [HttpGet("complete")]
    [AllowAnonymous]
    public async Task<IActionResult> LinkComplete(CancellationToken cancellationToken)
    {
        NoCache();
        if (!Enabled()) return Unavailable();
        try
        {
            var remote = await HttpContext.AuthenticateAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
            if (!remote.Succeeded || remote.Principal is null ||
                remote.Properties?.Items.TryGetValue("tindy:provider", out var provider) != true ||
                remote.Properties.Items.TryGetValue(TransactionItem, out var rawId) != true ||
                !Guid.TryParse(rawId, out var transactionId))
                return FrontendError("oauth_link_failed");
            var identity = AuthenticatedExternalIdentityReader.Read(remote.Principal, provider);
            if (identity is null) return FrontendError("oauth_link_failed");
            var ticket = await _connections.FinishProviderAsync(transactionId, identity, cancellationToken);
            if (ticket is null) return FrontendError("oauth_link_expired");
            return Redirect(FrontendCallbackBase() + "#link_ticket=" + Uri.EscapeDataString(ticket));
        }
        finally
        {
            if (await _schemes.GetSchemeAsync(OAuthAuthenticationExtensions.ExternalCookieScheme) is not null)
                await HttpContext.SignOutAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
        }
    }

    [HttpPost("confirm")]
    [Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
    [EnableRateLimiting("oauth-exchange")]
    public async Task<IActionResult> Confirm([FromBody] OAuthLinkConfirmRequest request,
        CancellationToken cancellationToken)
    {
        NoCache();
        if (!Enabled()) return Unavailable();
        var created = await _connections.ConfirmAsync(CurrentUserId(), request.LinkTicket, cancellationToken);
        return Ok(new { success = true, message = created ? "Account linked." : "Account was already linked.",
            data = new { linked = true, created } });
    }

    [HttpDelete("{provider}")]
    [Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
    public async Task<IActionResult> Unlink(string provider, CancellationToken cancellationToken)
    {
        NoCache();
        if (!TryProvider(provider, out var normalized)) return UnsupportedProvider();
        await _connections.UnlinkAsync(CurrentUserId(), normalized, cancellationToken);
        return Ok(new { success = true, message = "Provider disconnected.", data = (object?)null });
    }

    private Guid CurrentUserId()
    {
        var rawId = User.FindFirstValue("sub");
        if (!Guid.TryParse(rawId, out var userId)) throw new InvalidCredentialsException();
        return userId;
    }

    private bool Enabled() => _configuration.GetValue<bool>("OAuth:Enabled");
    private ObjectResult Unavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Service Unavailable",
        detail: "OAuth is unavailable.",
        extensions: new Dictionary<string, object?> { ["error_code"] = "OAUTH_UNAVAILABLE" });

    private ObjectResult UnsupportedProvider() => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Bad Request",
        detail: "Unsupported provider.",
        extensions: new Dictionary<string, object?> { ["error_code"] = "OAUTH_PROVIDER_UNSUPPORTED" });

    private static bool TryProvider(string value, out string provider)
    {
        provider = string.Empty;
        if (string.Equals(value, ExternalAuthProviders.Google, StringComparison.OrdinalIgnoreCase))
            provider = ExternalAuthProviders.Google;
        else if (string.Equals(value, ExternalAuthProviders.GitHub, StringComparison.OrdinalIgnoreCase))
            provider = ExternalAuthProviders.GitHub;
        return provider.Length != 0;
    }

    private void NoCache()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Pragma"] = "no-cache";
    }

    private IActionResult FrontendError(string code) =>
        Redirect(FrontendCallbackBase() + "?flow=link&error=" + Uri.EscapeDataString(code));

    private string FrontendCallbackBase()
    {
        var origins = _configuration.GetSection("OAuth:Frontend:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length == 0) throw new InvalidOperationException("OAuth frontend allowlist is missing.");
        return origins[0].TrimEnd('/') + "/auth/oauth/callback";
    }
}
