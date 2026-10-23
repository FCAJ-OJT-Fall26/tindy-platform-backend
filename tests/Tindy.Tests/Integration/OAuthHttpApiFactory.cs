using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.DTOs.Onboarding;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;
using Tindy.Application.Models.Authorization;

namespace Tindy.Tests.Integration;

/// <summary>
/// Exercises the actual HTTP pipeline, JWT validation, OAuth middleware, cookies and
/// serialization without PostgreSQL or the internet. Application service doubles
/// isolate HTTP contracts; database invariants have separate PostgreSQL tests.
/// </summary>
internal sealed class OAuthHttpApiFactory(bool oauthEnabled = true) : WebApplicationFactory<Program>
{
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public Guid UserId { get; } = Guid.NewGuid();
    public HttpAccountState Accounts { get; } = new();
    public HttpOAuthFlow Flow { get; } = new();
    public HttpConnections Connections { get; } = new();
    public ProviderBackchannel Provider { get; } = new();
    public CapturedLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var settings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Tindy.HttpTests",
                ["Jwt:Audience"] = "Tindy.HttpTests.Client",
                ["Jwt:SigningKey"] = _jwtKey,
                ["Jwt:AccessTokenLifetimeMinutes"] = "60",
                ["Jwt:ClockSkewSeconds"] = "0",
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=unused_http_tests;Username=unused",
                ["OAuth:Enabled"] = oauthEnabled.ToString(),
                ["MediaLifecycle:Enabled"] = "false",
                ["GuardDuty:Enabled"] = "false",
                ["OAuth:Google:ClientId"] = "http-test-google-client",
                ["OAuth:Google:ClientSecret"] = "http-test-google-placeholder",
                ["OAuth:GitHub:ClientId"] = "http-test-github-client",
                ["OAuth:GitHub:ClientSecret"] = "http-test-github-placeholder",
                ["OAuth:Frontend:AllowedOrigins:0"] = "http://localhost:8443",
                ["Cors:AllowedOrigins:0"] = "http://localhost:8443"
            };
        // Host settings are available before Program captures authentication settings;
        // the in-memory provider also keeps runtime IConfiguration/options consistent.
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Logs)
            .SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IAuthService>();
            services.RemoveAll<IOAuthLoginFlowService>();
            services.RemoveAll<IAccountConnectionsService>();
            services.RemoveAll<IOnboardingAccessReader>();
            services.RemoveAll<IOnboardingService>();
            services.AddScoped<IAuthService>(sp => new HttpAuthService(UserId,
                sp.GetRequiredService<IJwtTokenService>()));
            services.AddSingleton<IOAuthLoginFlowService>(Flow);
            services.AddSingleton<IAccountConnectionsService>(Connections);
            services.AddSingleton<IOnboardingAccessReader>(Accounts);
            services.AddSingleton<IOnboardingService>(new HttpOnboardingService(UserId));
            if (oauthEnabled)
            {
                services.PostConfigure<GoogleOptions>("Google", options =>
                    options.Backchannel = new HttpClient(Provider, disposeHandler: false));
                services.PostConfigure<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>("GitHub", options =>
                    options.Backchannel = new HttpClient(Provider, disposeHandler: false));
            }
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false,
        HandleCookies = true
    });

    public string TokenFor(Guid? userId = null)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(userId ?? UserId, ["Member"]).AccessToken;
    }
}

internal sealed class HttpAccountState : IOnboardingAccessReader
{
    public OnboardingAccessStatus Status { get; set; } = OnboardingAccessStatus.Incomplete;
    public Task<OnboardingAccessStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);
}

internal sealed class HttpAuthService(Guid userId, IJwtTokenService jwt) : IAuthService
{
    public Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var token = jwt.GenerateAccessToken(userId, ["Member"]);
        return Task.FromResult(new RegisterResponse(token.AccessToken, token.TokenType, token.ExpiresIn,
            new(userId, request.DisplayName!, request.University!, request.Email!, "test-user", "active", ["Member"], false),
            "/onboarding"));
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Password != "Password123!") throw new InvalidCredentialsException();
        var token = jwt.GenerateAccessToken(userId, ["Member"]);
        return Task.FromResult(new LoginResponse(token.AccessToken, token.TokenType, token.ExpiresIn,
            new(userId, "HTTP User", "FPT University", "http@example.com", "test-user", "active", ["Member"], false),
            "/onboarding"));
    }
}

internal sealed class HttpOAuthFlow : IOAuthLoginFlowService
{
    public const string LoginTicket = "opaque-login-ticket-canary";
    public const string TindyToken = "tindy-body-token-canary";
    public AuthenticatedExternalIdentity? LastIdentity { get; private set; }
    public string RedirectTo { get; set; } = "/onboarding";

    public Task<string> BeginTicketAsync(AuthenticatedExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        LastIdentity = identity;
        return Task.FromResult(LoginTicket);
    }

    public Task<OAuthExchangeResponse> ExchangeAsync(string ticket, CancellationToken cancellationToken = default)
    {
        if (ticket != LoginTicket) throw new InvalidCredentialsException();
        return Task.FromResult(new OAuthExchangeResponse(TindyToken, "Bearer", 3600,
            new(Guid.NewGuid(), "OAuth User", "", null, null, "active", ["Member"], false), RedirectTo));
    }
}

internal sealed class HttpConnections : IAccountConnectionsService
{
    public const string StartToken = "opaque-link-start-canary";
    public const string LinkTicket = "opaque-link-ticket-canary";
    public Guid TransactionId { get; } = Guid.NewGuid();
    public Exception? ListFailure { get; set; }
    public AuthenticatedExternalIdentity? LastIdentity { get; private set; }
    public Guid? StartedBy { get; private set; }
    public Task<AccountConnectionsResult> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (ListFailure is not null) throw ListFailure;
        return Task.FromResult(new AccountConnectionsResult(true,
            [new("Google", null, DateTime.UnixEpoch)]));
    }
    public Task<string> BeginAsync(Guid userId, string provider, CancellationToken cancellationToken = default)
    {
        StartedBy = userId;
        return Task.FromResult(StartToken);
    }
    public Task<Guid?> BeginChallengeAsync(string startToken, string provider, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(startToken == StartToken ? TransactionId : null);
    public Task<string?> FinishProviderAsync(Guid transactionId, AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default)
    {
        LastIdentity = identity;
        return Task.FromResult<string?>(transactionId == TransactionId ? LinkTicket : null);
    }
    public Task<bool> ConfirmAsync(Guid userId, string linkTicket, CancellationToken cancellationToken = default)
    {
        if (userId != StartedBy || linkTicket != LinkTicket) throw new InvalidCredentialsException();
        return Task.FromResult(true);
    }
    public Task UnlinkAsync(Guid userId, string provider, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class HttpOnboardingService(Guid userId) : IOnboardingService
{
    public Task<CurrentUserResponse> GetCurrentUserAsync(Guid id, CancellationToken ct = default) => Task.FromResult(
        new CurrentUserResponse(userId, null, "HTTP User", null, null, "active", ["Member"], false));
    public Task<OnboardingDraftResponse> GetDraftAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(new OnboardingDraftResponse(8, [], 1, false, new Dictionary<string, object>()));
    public Task<OnboardingSaveResponse> SaveStep1Async(Guid id, OnboardingStep1Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingSaveResponse> SaveStep2Async(Guid id, OnboardingStep2Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingSaveResponse> SaveStep3Async(Guid id, OnboardingStep3Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingSaveResponse> SaveStep4Async(Guid id, OnboardingStep4Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingSaveResponse> SaveStep5Async(Guid id, OnboardingStep5Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingSaveResponse> SaveStep6Async(Guid id, OnboardingStep6Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingSaveResponse> SaveStep7Async(Guid id, OnboardingStep7Request request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OnboardingCompleteResponse> CompleteAsync(Guid id, OnboardingCompleteRequest request, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Only known provider endpoints are accepted; an unexpected call fails closed.</summary>
internal sealed class ProviderBackchannel : HttpMessageHandler
{
    public const string ProviderToken = "provider-access-token-canary";
    public ConcurrentQueue<string> Requests { get; } = new();
    public bool FailTokenExchange { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Enqueue(uri.GetLeftPart(UriPartial.Path));
        if (uri.AbsolutePath.Contains("token", StringComparison.Ordinal))
        {
            if (FailTokenExchange) return Task.FromResult(Json(HttpStatusCode.BadRequest,
                new { error = "provider_failure", error_description = "provider-private-error-canary" }));
            return Task.FromResult(Json(HttpStatusCode.OK,
                new { access_token = ProviderToken, token_type = "Bearer", expires_in = 3600 }));
        }
        if (uri.Host.EndsWith("googleapis.com", StringComparison.Ordinal) && uri.AbsolutePath.Contains("userinfo"))
            return Task.FromResult(Json(HttpStatusCode.OK,
                new { id = "google-http-user", sub = "google-http-user", name = "Google HTTP User", email = "google@example.com", verified_email = true, email_verified = true }));
        if (uri.Host == "api.github.com" && uri.AbsolutePath == "/user")
            return Task.FromResult(Json(HttpStatusCode.OK,
                new { id = 45678, login = "github-http-user", name = "GitHub HTTP User", email = (string?)null }));
        if (uri.Host == "api.github.com" && uri.AbsolutePath == "/user/emails")
            return Task.FromResult(Json(HttpStatusCode.OK, Array.Empty<object>()));
        throw new InvalidOperationException("Unexpected provider backchannel endpoint in offline test.");
    }
    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturedLogger(Entries, categoryName);
    public void Dispose() { }
    private sealed class CapturedLogger(ConcurrentQueue<string> entries, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Enqueue(
                $"{categoryName} [{logLevel}/{eventId.Id}]: {formatter(state, exception)}{exception}");
    }
}
