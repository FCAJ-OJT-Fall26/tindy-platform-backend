using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Tindy.Tests.Integration;

/// <summary>Docker-free HTTP contracts; provider responses are simulated locally.</summary>
[Trait("Category", "DockerFree")]
public sealed class OAuthHttpContractTests
{
    [Theory]
    [InlineData("http@example.com")]
    [InlineData("test-user")]
    public async Task LocalAuth_UsesFrontendEnvelopeAndSnakeCase(string identifier)
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "HTTP User", university = "FPT University", email = "http@example.com",
            password = "Password123!", accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registered = await EnvelopeAsync(registration);
        Assert.Equal("/onboarding", registered.RootElement.GetProperty("data").GetProperty("redirect_to").GetString());
        AssertNoStore(registration);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier, password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loggedIn = await EnvelopeAsync(login);
        var data = loggedIn.RootElement.GetProperty("data");
        Assert.Equal("Bearer", data.GetProperty("token_type").GetString());
        Assert.False(data.TryGetProperty("accessToken", out _));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("access_token").GetString()));
        AssertNoStore(login);
        Assert.Null(login.Headers.Location);
    }

    [Fact]
    public async Task InvalidLocalLogin_ReturnsSafeProblemDetails()
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        { identifier = "http@example.com", password = "incorrect-password-canary" });
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
        Assert.DoesNotContain("incorrect-password-canary", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains("incorrect-password-canary"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Logout_RequiresBearer_AndLeavesIssuedJwtUsable(bool oauthEnabled)
    {
        using var factory = new OAuthHttpApiFactory(oauthEnabled);
        using var client = factory.CreateHttpsClient();
        await AssertProblemAsync(await client.PostAsync("/api/auth/logout", null),
            HttpStatusCode.Unauthorized, "AUTH_UNAUTHORIZED");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.TokenFor());
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var body = await EnvelopeAsync(logout);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        AssertNoStore(logout);
        if (oauthEnabled)
            Assert.Contains(logout.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-Tindy.External=;"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task PendingUser_CanReadMeAndConnections_ButBusinessGateRequiresOnboarding()
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.TokenFor());
        using var me = await EnvelopeAsync(await client.GetAsync("/api/me"));
        Assert.Equal(factory.UserId, me.RootElement.GetProperty("data").GetProperty("user_id").GetGuid());
        var connections = await client.GetAsync("/api/auth/connections");
        using var body = await EnvelopeAsync(connections);
        Assert.True(body.RootElement.GetProperty("data").GetProperty("has_local_password").GetBoolean());
        Assert.Equal("Google", body.RootElement.GetProperty("data").GetProperty("providers")[0].GetProperty("provider").GetString());
        AssertNoStore(connections);
        await AssertProblemAsync(await client.GetAsync("/api/dev/onboarding-gate-check"),
            HttpStatusCode.Forbidden, "ONBOARDING_REQUIRED");
        factory.Accounts.Status = Tindy.Application.Models.Authorization.OnboardingAccessStatus.Completed;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dev/onboarding-gate-check")).StatusCode);
        factory.Accounts.Status = Tindy.Application.Models.Authorization.OnboardingAccessStatus.InactiveOrMissing;
        await AssertProblemAsync(await client.GetAsync("/api/me"), HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");
        await AssertProblemAsync(await client.PostAsync("/api/auth/logout", null), HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");
        await AssertProblemAsync(await client.GetAsync("/api/auth/connections"), HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");
    }

    [Theory]
    [InlineData("http://localhost:8443", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task CorsPreflight_OnlyAllowsConfiguredOrigins_WithAuthorizationHeader(string origin, bool allowed)
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/connections/google/start");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        if (allowed)
        {
            Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Contains("authorization", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("google", "Google", "/signin-google")]
    [InlineData("github", "GitHub", "/signin-github")]
    public async Task ProviderLogin_UsesActualStateCorrelationAndCookie_ThenOnlyOpaqueFragmentTicket(
        string route, string provider, string callback)
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        var challenge = await client.GetAsync($"/api/auth/{route}?returnUrl=https://untrusted.example");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        AssertNoStore(challenge);
        Assert.Equal("no-referrer", challenge.Headers.GetValues("Referrer-Policy").Single());
        var authorize = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Assert.EndsWith(callback, authorize["redirect_uri"].ToString());
        Assert.False(authorize.ContainsKey("access_token"));
        Assert.Contains(challenge.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains(".AspNetCore.Correlation.") && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));

        var providerCallback = await client.GetAsync(QueryHelpers.AddQueryString(callback,
            new Dictionary<string, string?> { ["code"] = "offline-provider-code", ["state"] = authorize["state"] }));
        Assert.Equal(HttpStatusCode.Redirect, providerCallback.StatusCode);
        var completed = await client.GetAsync(providerCallback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.Equal($"http://localhost:8443/auth/oauth/callback#ticket={HttpOAuthFlow.LoginTicket}", completed.Headers.Location!.OriginalString);
        Assert.Equal(provider, factory.Flow.LastIdentity!.Provider);
        if (provider == "GitHub") Assert.Null(factory.Flow.LastIdentity.Email);
        AssertNoStore(completed);
        Assert.Contains(factory.Provider.Requests, uri => uri.Contains("token"));
        Assert.DoesNotContain(ProviderBackchannel.ProviderToken, completed.Headers.Location.OriginalString);
        Assert.DoesNotContain(HttpOAuthFlow.TindyToken, completed.Headers.Location.OriginalString);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains(ProviderBackchannel.ProviderToken) || entry.Contains(HttpOAuthFlow.LoginTicket));

        // The temporary external cookie has been deleted; completion cannot be replayed.
        var repeated = await client.GetAsync("/api/auth/oauth/complete");
        Assert.Equal("http://localhost:8443/auth/oauth/callback?error=oauth_failed", repeated.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/onboarding")]
    [InlineData("/discover")]
    public async Task Exchange_ReturnsJwtOnlyInBody_WithNullableEmailAndNoStore(string redirectTo)
    {
        using var factory = new OAuthHttpApiFactory();
        factory.Flow.RedirectTo = redirectTo;
        using var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/oauth/exchange", new { ticket = HttpOAuthFlow.LoginTicket });
        using var body = await EnvelopeAsync(response);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(HttpOAuthFlow.TindyToken, data.GetProperty("access_token").GetString());
        Assert.Equal(redirectTo, data.GetProperty("redirect_to").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("user").GetProperty("email").ValueKind);
        Assert.Null(response.Headers.Location);
        AssertNoStore(response);
        Assert.DoesNotContain(ProviderBackchannel.ProviderToken, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Linking_UsesJwtStart_ProtectedProviderCallback_AndSameUserConfirmation()
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        await AssertProblemAsync(await client.PostAsync("/api/auth/connections/google/start", null),
            HttpStatusCode.Unauthorized, "AUTH_UNAUTHORIZED");
        var jwt = factory.TokenFor();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var start = await client.PostAsync("/api/auth/connections/google/start", null);
        using var startBody = await EnvelopeAsync(start);
        var url = startBody.RootElement.GetProperty("data").GetProperty("authorization_url").GetString()!;
        Assert.DoesNotContain(jwt, url);
        Assert.Contains("request=" + HttpConnections.StartToken, url);
        client.DefaultRequestHeaders.Authorization = null;
        var challenge = await client.GetAsync(url);
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        var callback = await client.GetAsync(QueryHelpers.AddQueryString("/signin-google",
            new Dictionary<string, string?> { ["code"] = "offline-provider-code", ["state"] = state }));
        var complete = await client.GetAsync(callback.Headers.Location);
        Assert.Equal($"http://localhost:8443/auth/oauth/callback#link_ticket={HttpConnections.LinkTicket}", complete.Headers.Location!.OriginalString);
        Assert.Equal("Google", factory.Connections.LastIdentity!.Provider);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.TokenFor(Guid.NewGuid()));
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/connections/confirm", new { link_ticket = HttpConnections.LinkTicket }),
            HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        using var confirm = await EnvelopeAsync(await client.PostAsJsonAsync("/api/auth/connections/confirm", new { link_ticket = HttpConnections.LinkTicket }));
        Assert.True(confirm.RootElement.GetProperty("data").GetProperty("linked").GetBoolean());
        using var unlink = await EnvelopeAsync(await client.DeleteAsync("/api/auth/connections/google"));
        Assert.Equal(JsonValueKind.Null, unlink.RootElement.GetProperty("data").ValueKind);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains(jwt) || entry.Contains(HttpConnections.LinkTicket) || entry.Contains(HttpConnections.StartToken));
    }

    [Theory]
    [InlineData("/api/auth/google")]
    [InlineData("/api/auth/github")]
    [InlineData("/api/auth/oauth/complete")]
    public async Task DisabledOAuth_ReturnsProblemDetails(string path)
    {
        using var factory = new OAuthHttpApiFactory(false);
        using var client = factory.CreateHttpsClient();
        await AssertProblemAsync(await client.GetAsync(path), HttpStatusCode.ServiceUnavailable, "OAUTH_UNAVAILABLE");
    }

    [Fact]
    public async Task DisabledOAuth_ExchangeAndLinkStart_ReturnProblemDetails()
    {
        using var factory = new OAuthHttpApiFactory(false);
        using var client = factory.CreateHttpsClient();
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/oauth/exchange", new { ticket = "invalid" }),
            HttpStatusCode.ServiceUnavailable, "OAUTH_UNAVAILABLE");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.TokenFor());
        await AssertProblemAsync(await client.PostAsync("/api/auth/connections/google/start", null),
            HttpStatusCode.ServiceUnavailable, "OAUTH_UNAVAILABLE");
    }

    [Fact]
    public async Task InvalidProviderAndLinkRequest_ReturnSafeProblemDetails()
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.TokenFor());
        await AssertProblemAsync(await client.PostAsync("/api/auth/connections/unknown/start", null),
            HttpStatusCode.BadRequest, "OAUTH_PROVIDER_UNSUPPORTED");
        await AssertProblemAsync(await client.DeleteAsync("/api/auth/connections/unknown"),
            HttpStatusCode.BadRequest, "OAUTH_PROVIDER_UNSUPPORTED");
        client.DefaultRequestHeaders.Authorization = null;
        await AssertProblemAsync(await client.GetAsync("/api/auth/connections/google/challenge?request=invalid"),
            HttpStatusCode.BadRequest, "OAUTH_LINK_REQUEST_INVALID");
    }

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    public async Task CancellationInvalidStateAndMissingCorrelation_ReturnSafeHandshakeProblem(string provider)
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        var callback = "/signin-" + provider;
        await AssertProblemAsync(await client.GetAsync(callback + "?code=private-code-canary&state=invalid-state-canary"),
            HttpStatusCode.BadRequest, "OAUTH_HANDSHAKE_FAILED");

        var challenge = await client.GetAsync("/api/auth/" + provider);
        await AssertRedirectAsync(challenge);
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        var cancelled = await client.GetAsync(QueryHelpers.AddQueryString(callback,
            new Dictionary<string, string?> { ["error"] = "access_denied", ["error_description"] = "provider-private-description-canary", ["state"] = state }));
        await AssertProblemAsync(cancelled, HttpStatusCode.BadRequest, "OAUTH_HANDSHAKE_FAILED");
        AssertNoStore(cancelled);
        Assert.DoesNotContain("provider-private-description-canary", await cancelled.Content.ReadAsStringAsync());

        challenge = await client.GetAsync("/api/auth/" + provider);
        await AssertRedirectAsync(challenge);
        state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        using var withoutCookie = factory.CreateHttpsClient();
        await AssertProblemAsync(await withoutCookie.GetAsync(QueryHelpers.AddQueryString(callback,
            new Dictionary<string, string?> { ["code"] = "private-code-canary", ["state"] = state })),
            HttpStatusCode.BadRequest, "OAUTH_HANDSHAKE_FAILED");
        Assert.Empty(factory.Provider.Requests);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains("private-code-canary") || entry.Contains("provider-private-description-canary"));
    }

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    public async Task ProviderTokenEndpointFailure_DoesNotExposeProviderBody(string provider)
    {
        using var factory = new OAuthHttpApiFactory();
        factory.Provider.FailTokenExchange = true;
        using var client = factory.CreateHttpsClient();
        var challenge = await client.GetAsync("/api/auth/" + provider);
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        var failed = await client.GetAsync(QueryHelpers.AddQueryString("/signin-" + provider,
            new Dictionary<string, string?> { ["code"] = "private-code-canary", ["state"] = state }));
        await AssertProblemAsync(failed, HttpStatusCode.BadRequest, "OAUTH_HANDSHAKE_FAILED");
        Assert.Contains(factory.Provider.Requests, uri => uri.Contains("token"));
        Assert.DoesNotContain("provider-private-error-canary", await failed.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains("provider-private-error-canary") || entry.Contains("private-code-canary"));
    }

    [Fact]
    public async Task UnexpectedException_ProducesGenericProblem_WithoutLoggingSensitiveMessage()
    {
        using var factory = new OAuthHttpApiFactory();
        factory.Connections.ListFailure = new InvalidOperationException("sensitive-token-canary");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.TokenFor());
        var response = await client.GetAsync("/api/auth/connections");
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        Assert.DoesNotContain("sensitive-token-canary", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Contains("sensitive-token-canary"));
    }

    [Theory]
    [InlineData("/api/auth/google", 20, false)]
    [InlineData("/api/auth/oauth/exchange", 10, true)]
    [InlineData("/api/auth/login", 10, true)]
    public async Task AuthenticationRateLimits_Return429Problem(string path, int permits, bool post)
    {
        using var factory = new OAuthHttpApiFactory();
        using var client = factory.CreateHttpsClient();
        HttpResponseMessage? response = null;
        for (var i = 0; i <= permits; i++)
        {
            response?.Dispose();
            response = post
                ? await client.PostAsJsonAsync(path, new { ticket = "invalid", identifier = "test-user", password = "wrong" })
                : await client.GetAsync(path);
            if (i < permits) Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using (response)
            await AssertProblemAsync(response!, HttpStatusCode.TooManyRequests, "AUTH_TOO_MANY_ATTEMPTS");
    }

    private static async Task<JsonDocument> EnvelopeAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
        Assert.True(body.RootElement.TryGetProperty("data", out _));
        return body;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("error_code").GetString());
    }

    private static async Task AssertRedirectAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"Expected redirect, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.NotNull(response.Headers.Location);
    }

    private static void AssertNoStore(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore);
}
