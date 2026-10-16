using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Constants;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class AuthenticationDatabaseCompatibilityTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;

    public AuthenticationDatabaseCompatibilityTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LocalRegister_SetsPrivateEmail_ButDoesNotMarkItVerified()
    {
        using var client = _factory.CreateHttpsClient();
        var email = $"localprimary.{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Local User",
            university = "FPT University",
            email,
            password = "Password123!",
            accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var user = await db.Users.AsNoTracking()
            .Include(x => x.Credential)
            .SingleAsync(x => x.PrimaryEmailNormalized == email);

        Assert.Equal(email, user.PrimaryEmail);
        Assert.Null(user.PrimaryEmailVerifiedAt);
        Assert.NotNull(user.Credential);
        Assert.Equal(user.Credential!.EmailNormalized, user.PrimaryEmailNormalized);
        Assert.Null(user.Credential.EmailVerifiedAt);
    }

    [Fact]
    public async Task OAuthOnlyUser_WithoutEmailOrCredential_CanAccessMeAndOnboarding()
    {
        var userId = Guid.NewGuid();
        string accessToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            var memberRole = await db.Roles.AsNoTracking()
                .SingleAsync(x => x.RoleName == RoleNames.Member);

            db.Users.Add(new User
            {
                UserId = userId,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                IsOnboardingCompleted = false,
                // No primary email, credential, or password.
                Profile = new UserProfile
                {
                    UserId = userId,
                    DisplayName = "GitHub User",
                    University = null,
                    CreatedAt = DateTime.UtcNow
                },
                ExternalAuthAccounts = new List<ExternalAuthAccount>
                {
                    new()
                    {
                        ExternalAuthAccountId = Guid.NewGuid(),
                        UserId = userId,
                        Provider = "GitHub",
                        ProviderUserId = Guid.NewGuid().ToString("N"),
                        CreatedAt = DateTime.UtcNow
                    }
                },
                UserRoles = new List<UserRole>
                {
                    new() { UserId = userId, RoleId = memberRole.RoleId }
                }
            });
            await db.SaveChangesAsync();

            var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            accessToken = tokenService.GenerateAccessToken(userId, new[] { RoleNames.Member }).AccessToken;
        }

        using var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(userId.ToString(), data.GetProperty("user_id").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("email").ValueKind);

        var onboarding = await client.GetAsync("/api/me/onboarding");
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
    }
}
