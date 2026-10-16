using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Api.Extensions;
using Tindy.Application.Constants;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;

namespace Tindy.Api.Controllers;

/// <summary>
/// STEP 04: server-side OAuth code flow, short-lived one-time ticket, JWT exchange.
/// Middleware owns /signin-google and /signin-github; these are NOT MVC routes.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class OAuthController : ControllerBase
{
    private readonly IOAuthLoginFlowService _flow;
    private readonly IConfiguration _configuration;
    private readonly IAuthenticationSchemeProvider _schemes;

    public OAuthController(IOAuthLoginFlowService flow, IConfiguration configuration,
        IAuthenticationSchemeProvider schemes)
    {
        _flow = flow;
        _configuration = configuration;
        _schemes = schemes;
    }

    [HttpGet("google")]
    [EnableRateLimiting("oauth-start")]
    public Task<IActionResult> Google() => StartAsync(ExternalAuthProviders.Google);

    [HttpGet("github")]
    [EnableRateLimiting("oauth-start")]
    public Task<IActionResult> GitHub() => StartAsync(ExternalAuthProviders.GitHub);

    private async Task<IActionResult> StartAsync(string provider)
    {
        if (!_configuration.GetValue<bool>("OAuth:Enabled") ||
            await _schemes.GetSchemeAsync(provider) is null)
        {
            return Unavailable();
        }

        // Prevent a prior remote principal from being mistaken for this new challenge.
        await HttpContext.SignOutAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.ActionLink(nameof(Complete), "OAuth")
                ?? "/api/auth/oauth/complete"
        };
        properties.Items["tindy:provider"] = provider;
        // Correlation and state are protected by the OAuth middleware.
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Challenge(properties, provider);
    }

    /// <summary>Invoked AFTER middleware consumes provider code at /signin-xxx.</summary>
    [HttpGet("oauth/complete")]
    public async Task<IActionResult> Complete(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue<bool>("OAuth:Enabled"))
            return Unavailable();

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Pragma"] = "no-cache";
        try
        {
            var remote = await HttpContext.AuthenticateAsync(
                OAuthAuthenticationExtensions.ExternalCookieScheme);
            if (!remote.Succeeded || remote.Principal is null ||
                remote.Properties?.Items.TryGetValue("tindy:provider", out var provider) != true ||
                (provider != ExternalAuthProviders.Google && provider != ExternalAuthProviders.GitHub))
                return SafeFrontendError("oauth_failed");

            // Read only claims validated by the Google/GitHub middleware.
            var identity = AuthenticatedExternalIdentityReader.Read(remote.Principal, provider);
            if (identity is null) return SafeFrontendError("oauth_failed");

            string ticket;
            try
            {
                ticket = await _flow.BeginTicketAsync(identity, cancellationToken);
            }
            catch (ConflictException)
            {
                // Never merge by email; frontend should direct the user to sign in
                // to their existing Tindy account before explicit linking (STEP 05).
                return SafeFrontendError("account_link_required");
            }
            catch (InvalidCredentialsException)
            {
                return SafeFrontendError("account_unavailable");
            }

            // Fragment isn't sent to the frontend web server in an HTTP request.
            // It must be immediately removed from browser history by STEP 07 frontend.
            return Redirect(FrontendCallbackBase() + "#ticket=" + Uri.EscapeDataString(ticket));
        }
        finally
        {
            // Never keep a provider sign-in cookie beyond the OAuth handshake.
            if (_configuration.GetValue<bool>("OAuth:Enabled") &&
                await _schemes.GetSchemeAsync(OAuthAuthenticationExtensions.ExternalCookieScheme) is not null)
                await HttpContext.SignOutAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
        }
    }

    [HttpPost("oauth/exchange")]
    [EnableRateLimiting("oauth-exchange")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Exchange([FromBody] OAuthExchangeRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Pragma"] = "no-cache";
        if (!_configuration.GetValue<bool>("OAuth:Enabled"))
            return Unavailable();

        var result = await _flow.ExchangeAsync(request.Ticket, cancellationToken);
        return Ok(new { success = true, message = "Logged in successfully.", data = result });
    }

    private IActionResult SafeFrontendError(string code) =>
        Redirect(FrontendCallbackBase() + "?error=" + Uri.EscapeDataString(code));

    private ObjectResult Unavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Service Unavailable",
        detail: "OAuth sign-in is unavailable.",
        extensions: new Dictionary<string, object?> { ["error_code"] = "OAUTH_UNAVAILABLE" });

    private string FrontendCallbackBase()
    {
        // A fixed server-configured allowlist entry, never an arbitrary returnUrl.
        var origins = _configuration.GetSection("OAuth:Frontend:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length == 0)
            throw new InvalidOperationException("OAuth frontend allowlist is not configured.");
        return origins[0].TrimEnd('/') + "/auth/oauth/callback";
    }

}
