using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class MemberSearchHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private async Task<(HttpClient Client, User[] Users, string Prefix)> Setup()
    {
        var prefix = "Lookup" + Guid.NewGuid().ToString("N")[..6];
        var users = Enumerable.Range(0, 7).Select(i => new User {
            UserId = Guid.NewGuid(), Username = prefix + "_" + i, UsernameNormalized = (prefix + "_" + i).ToLowerInvariant(),
            Status = i == 3 ? "inactive" : "active", DeletedAt = i == 4 ? DateTime.UtcNow : null,
            IsOnboardingCompleted = i != 5, CreatedAt = DateTime.UtcNow,
            PrimaryEmail = "private@example.test",
            Profile = i == 6 ? null : new UserProfile {
                DisplayName = prefix + (i == 2 ? "%_special" : " Member " + i), University = "FPT University",
                Headline = "Student developer", PhoneNumber = "0123456789", PublicEmail = "public@example.test",
                OpenToTeamInvitations = false, CreatedAt = DateTime.UtcNow }
        }).ToArray();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(users[0].UserId, ["Member"]).AccessToken);
        return (client, users, prefix);
    }

    [Fact]
    public async Task Search_ByNameOrUsername_IsCaseInsensitive_EligibleOnly_AndUsesPublicFields()
    {
        var (client, users, prefix) = await Setup();
        using (client)
        {
            using var response = await client.GetAsync("/api/users?q=" + prefix.ToUpperInvariant());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(3, items.Length);
            Assert.Equal(users.Take(3).Select(u => u.UserId).Order(), items.Select(i => i.GetProperty("user_id").GetGuid()).Order());
            foreach (var item in items)
                Assert.Equal(new[] { "display_name", "headline", "university", "user_id", "username" }, item.EnumerateObject().Select(p => p.Name).Order());
            using var username = await client.GetAsync("/api/users?q=" + users[1].Username);
            using var byUsername = JsonDocument.Parse(await username.Content.ReadAsStringAsync());
            Assert.Equal(users[1].UserId, Assert.Single(byUsername.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()).GetProperty("user_id").GetGuid());
            using var profile = await client.GetAsync($"/api/users/{users[1].UserId}/profile");
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        }
    }

    [Fact]
    public async Task Search_PaginatesAndEscapesWildcards_AndEmptyQueryDoesNotListEveryone()
    {
        var (client, users, prefix) = await Setup();
        using (client)
        {
            var seen = new HashSet<Guid>();
            for (var page = 1; page <= 3; page++)
            {
                using var response = await client.GetAsync($"/api/users?q={prefix}&page_size=1&page={page}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var data = json.RootElement.GetProperty("data");
                Assert.True(seen.Add(Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("user_id").GetGuid()));
                Assert.Equal(page < 3, data.GetProperty("has_more").GetBoolean());
            }
            using var literal = await client.GetAsync("/api/users?q=" + Uri.EscapeDataString(prefix + "%_"));
            using var special = JsonDocument.Parse(await literal.Content.ReadAsStringAsync());
            Assert.Equal(users[2].UserId, Assert.Single(special.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()).GetProperty("user_id").GetGuid());
            foreach (var query in new[] { "", "?q=x", "?q=%20%20", "?q=nobody" + prefix })
            {
                using var response = await client.GetAsync("/api/users" + query);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Empty(json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            }
        }
    }

    [Fact]
    public async Task Search_EnforcesAuthenticationOnboardingAndInputLimits()
    {
        using var anonymous = factory.CreateHttpsClient();
        using var unauthenticated = await anonymous.GetAsync("/api/users?q=Member");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        var (client, users, prefix) = await Setup();
        using (client)
        {
            foreach (var query in new[] { "q=" + new string('a', 101), "q=aa&page=0", "q=aa&page=1001", "q=aa&page_size=0", "q=aa&page_size=21" })
            {
                using var response = await client.GetAsync("/api/users?" + query);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            using var scope = factory.Services.CreateScope();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(users[5].UserId, ["Member"]).AccessToken);
            using var incomplete = await client.GetAsync("/api/users?q=" + prefix);
            Assert.Equal(HttpStatusCode.Forbidden, incomplete.StatusCode);
        }
    }
}
