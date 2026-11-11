using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tindy.Tests.Integration;

[Trait("Category", "DockerFree")]
public sealed class CloudFrontProxyTests
{
    private const string PublicHost = "demo.cloudfront.net";
    private const string AlbPeer = "10.20.30.10";

    [Theory]
    [InlineData("google", AlbPeer, "198.51.100.12:12345", "198.51.100.12")]
    [InlineData("github", "::ffff:" + AlbPeer, "[2001:db8::12]:12345", "2001:db8::12")]
    public async Task TrustedProxy_UsesViewerHttpsAndIpForOAuth(string provider, string peer, string address, string ip)
    {
        using var source = new OAuthHttpApiFactory();
        using var factory = source.WithWebHostBuilder(ConfigureProxy);

        var result = await SendAsync(factory.Server, $"/api/auth/{provider}", peer, address: address);

        Assert.Equal(StatusCodes.Status302Found, result.Response.StatusCode);
        var location = new Uri(result.Response.Headers.Location.ToString());
        Assert.Equal($"https://{PublicHost}/signin-{provider}",
            QueryHelpers.ParseQuery(location.Query)["redirect_uri"].ToString());
        Assert.Equal(ip, result.Connection.RemoteIpAddress?.ToString());
    }

    [Theory]
    [InlineData("127.0.0.1", PublicHost, "https", "198.51.100.12:12345")]
    [InlineData(null, PublicHost, "https", "198.51.100.12:12345")]
    [InlineData(AlbPeer, "spoofed.example", "https", "198.51.100.12:12345")]
    [InlineData(AlbPeer, PublicHost, "http", "198.51.100.12:12345")]
    [InlineData(AlbPeer, PublicHost, null, "198.51.100.12:12345")]
    [InlineData(AlbPeer, PublicHost, "https", "not-an-address")]
    [InlineData(AlbPeer, PublicHost, "https", "198.51.100.12:12345, 198.51.100.13:12345")]
    public async Task InvalidProxyRequest_CannotStartOAuth(string? peer, string host, string? protocol, string address)
    {
        using var source = new OAuthHttpApiFactory();
        using var factory = source.WithWebHostBuilder(ConfigureProxy);

        var result = await SendAsync(factory.Server, "/api/auth/google", peer, host, protocol, address);

        Assert.Equal(StatusCodes.Status400BadRequest, result.Response.StatusCode);
        Assert.False(result.Response.Headers.ContainsKey("Location"));
    }

    [Theory]
    [InlineData("/api/health", StatusCodes.Status200OK)]
    [InlineData("/api/health/live", StatusCodes.Status200OK)]
    [InlineData("/api/health/ready", StatusCodes.Status503ServiceUnavailable)]
    public async Task InternalHealthRequest_DoesNotRequireViewerHeaders(string route, int statusCode)
    {
        using var source = new OAuthHttpApiFactory();
        using var factory = source.WithWebHostBuilder(ConfigureProxy);

        var result = await SendAsync(factory.Server, route, AlbPeer, "10.20.40.11:8080", null, null);

        Assert.Equal(statusCode, result.Response.StatusCode);
        Assert.False(result.Response.Headers.ContainsKey("Location"));
    }

    private static void ConfigureProxy(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CloudFrontProxy:Enabled"] = "true",
            ["CloudFrontProxy:PublicHost"] = PublicHost,
            ["CloudFrontProxy:TrustedProxyNetworks:0"] = "10.20.30.0/24",
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1"
        }));

    private static Task<HttpContext> SendAsync(TestServer server, string path, string? peer,
        string host = PublicHost, string? protocol = "https", string? address = "198.51.100.12:12345") =>
        server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString(host);
            context.Request.Headers["X-Forwarded-Proto"] = "http";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.2";
            context.Request.Headers["X-Forwarded-Host"] = "spoofed.example";
            if (protocol is not null) context.Request.Headers["CloudFront-Forwarded-Proto"] = protocol;
            if (address is not null) context.Request.Headers["CloudFront-Viewer-Address"] = address;
        });
}
