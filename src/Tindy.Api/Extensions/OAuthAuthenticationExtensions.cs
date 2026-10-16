using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;

namespace Tindy.Api.Extensions;

/// <summary>
/// Registers Google and GitHub as remote identity providers.
/// The main Tindy API continues to authenticate with JWT Bearer.
/// </summary>
public static class OAuthAuthenticationExtensions
{
    public const string ExternalCookieScheme = "Tindy.External";
    public const string GoogleScheme = "Google";
    public const string GitHubScheme = "GitHub";

    public static AuthenticationBuilder AddTindyExternalOAuth(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        // Local Register/Login and JWT continue to work before OAuth credentials exist.
        if (!configuration.GetValue<bool>("OAuth:Enabled"))
        {
            return builder;
        }

        var googleClientId = RequireSecret(configuration, "OAuth:Google:ClientId");
        var googleClientSecret = RequireSecret(configuration, "OAuth:Google:ClientSecret");
        var gitHubClientId = RequireSecret(configuration, "OAuth:GitHub:ClientId");
        var gitHubClientSecret = RequireSecret(configuration, "OAuth:GitHub:ClientSecret");

        ValidateFrontendOriginAllowlist(configuration);

        return builder
            // Only used to transfer an authenticated external principal after the
            // remote callback. It is NOT the default API authentication mechanism.
            .AddCookie(ExternalCookieScheme, options =>
            {
                options.Cookie.Name = "__Host-Tindy.External";
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = false;
            })
            .AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
                options.SignInScheme = ExternalCookieScheme;
                options.CallbackPath = "/signin-google";
                options.SaveTokens = false;

                ConfigureCorrelationCookie(options);
                options.Events.OnRemoteFailure = HandleRemoteFailureAsync;
            })
            .AddGitHub(options =>
            {
                options.ClientId = gitHubClientId;
                options.ClientSecret = gitHubClientSecret;
                options.SignInScheme = ExternalCookieScheme;
                options.CallbackPath = "/signin-github";
                options.SaveTokens = false;

                // Required when the user's email is not publicly visible.
                // STEP 04 must still check whether each returned email is verified.
                if (!options.Scope.Contains("user:email"))
                {
                    options.Scope.Add("user:email");
                }

                ConfigureCorrelationCookie(options);
                options.Events.OnRemoteFailure = HandleRemoteFailureAsync;
            });
    }

    private static void ConfigureCorrelationCookie(
        Microsoft.AspNetCore.Authentication.RemoteAuthenticationOptions options)
    {
        // Remote OAuth callbacks are cross-site redirects.
        options.CorrelationCookie.HttpOnly = true;
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    }

    private static string RequireSecret(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"OAuth is enabled but '{key}' is missing. Configure it via User Secrets or a secret manager.");
        }

        return value;
    }

    private static void ValidateFrontendOriginAllowlist(IConfiguration configuration)
    {
        // STEP 04 will enforce these exact origins when building frontend redirects.
        var origins = configuration.GetSection("OAuth:Frontend:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        if (origins.Length == 0)
        {
            throw new InvalidOperationException(
                "OAuth:Frontend:AllowedOrigins must contain at least one trusted frontend origin.");
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == Uri.UriSchemeHttps ||
                  (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException(
                    "OAuth:Frontend:AllowedOrigins must contain only HTTPS origins " +
                    "(HTTP is permitted only for loopback development hosts), with no path or query.");
            }
        }
    }

    private static Task HandleRemoteFailureAsync(
        Microsoft.AspNetCore.Authentication.RemoteFailureContext context)
    {
        // Do not leak provider exception text, authorization codes, or tokens.
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers["Cache-Control"] = "no-store";
        return context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error_code = "OAUTH_HANDSHAKE_FAILED",
            message = "External sign-in was cancelled or could not be completed."
        });
    }
}
