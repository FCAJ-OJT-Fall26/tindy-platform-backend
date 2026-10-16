using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

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

        // The third-party GitHub handler uses a category outside Microsoft.AspNetCore.
        // Its inherited remote-failure Information log can contain provider response
        // bodies before OnRemoteFailure sanitizes the HTTP response.
        builder.Services.AddLogging(logging =>
            logging.AddFilter("AspNet.Security.OAuth.GitHub", LogLevel.Warning));

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
                // Google userinfo response may use either key depending on endpoint version.
                // Never infer verification from the presence of a nonempty email.
                options.ClaimActions.MapJsonKey("tindy:google_verified_v2", "verified_email");
                options.ClaimActions.MapJsonKey("tindy:google_verified_v3", "email_verified");

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
                options.Events.OnCreatingTicket = async context =>
                {
                    // GitHub's public email claim may be absent or unverified.
                    // Ask /user/emails and mark an email verified ONLY when confirmed.
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        "https://api.github.com/user/emails");
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", context.AccessToken);
                    request.Headers.UserAgent.ParseAdd("Tindy-Backend/1.0");
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using var response = await context.Backchannel.SendAsync(request,
                        context.HttpContext.RequestAborted);
                    if (!response.IsSuccessStatusCode) return; // Treat unknown as unverified.
                    await using var stream = await response.Content.ReadAsStreamAsync(
                        context.HttpContext.RequestAborted);
                    using var json = await JsonDocument.ParseAsync(stream,
                        cancellationToken: context.HttpContext.RequestAborted);
                    if (json.RootElement.ValueKind != JsonValueKind.Array) return;
                    var verified = json.RootElement.EnumerateArray()
                        .Where(x => x.TryGetProperty("verified", out var v) &&
                                    v.ValueKind == JsonValueKind.True &&
                                    x.TryGetProperty("email", out var e) &&
                                    e.ValueKind == JsonValueKind.String)
                        .OrderByDescending(x => x.TryGetProperty("primary", out var p) &&
                                                p.ValueKind == JsonValueKind.True)
                        .FirstOrDefault();
                    if (verified.ValueKind != JsonValueKind.Object) return;
                    var email = verified.GetProperty("email").GetString();
                    if (string.IsNullOrWhiteSpace(email)) return;
                    context.Identity?.AddClaim(new Claim("tindy:github_verified_email", email));
                };
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
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = "External sign-in was cancelled or could not be completed.",
            Instance = context.Request.Path
        };
        problem.Extensions["error_code"] = "OAUTH_HANDSHAKE_FAILED";
        return context.Response.WriteAsJsonAsync(problem,
            options: null, contentType: "application/problem+json",
            cancellationToken: context.HttpContext.RequestAborted);
    }
}
