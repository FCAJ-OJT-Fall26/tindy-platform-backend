using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tindy.Tests.Integration;

// Separate app/limiter instance from other integration tests.
public sealed class RateLimitTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public RateLimitTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_RejectsExcessiveAttempts_With429()
    {
        using var client = _factory.CreateHttpsClient();
        var responses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                identifier = "absent@example.com",
                password = "Password123!"
            });
            responses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, responses);
    }
}
