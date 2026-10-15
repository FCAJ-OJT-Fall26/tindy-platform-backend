using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class AuthenticationOnboardingTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public AuthenticationOnboardingTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_Login_CompleteOnboarding_AndGate_EndToEnd()
    {
        using var client = _factory.CreateHttpsClient();
        var email = $"step14.{Guid.NewGuid():N}@example.com";
        const string password = "Password123!";
        var username = "user" + Guid.NewGuid().ToString("N")[..14];
        var registerBody = new
        {
            display_name = "Step 14 Test",
            university = "FPT University",
            email,
            password,
            accept_terms = true
        };

        // STEP 08: registration + persisted user/profile/role + JWT.
        var registration = await client.PostAsJsonAsync("/api/auth/register", registerBody);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationJson = await ReadJsonAsync(registration);
        var token = registrationJson.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.False(registrationJson.RootElement.GetProperty("data").GetProperty("user").GetProperty("is_onboarding_completed").GetBoolean());

        // Duplicate registration must not create a second credential.
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", registerBody);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertErrorAsync(duplicate, "AUTH_EMAIL_ALREADY_EXISTS");

        // STEP 09: Login by email + wrong-password rejection.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = email,
            password = "WrongPassword123!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        await AssertErrorAsync(wrongPassword, "AUTH_INVALID_CREDENTIALS");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // STEP 13: uncompleted user cannot use business endpoints.
        var gateBefore = await client.GetAsync("/api/dev/onboarding-gate-check");
        Assert.Equal(HttpStatusCode.Forbidden, gateBefore.StatusCode);
        await AssertErrorAsync(gateBefore, "ONBOARDING_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/onboarding")).StatusCode);

        // STEP 12: Cannot complete before all seven step markers exist.
        var premature = await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, premature.StatusCode);
        await AssertErrorAsync(premature, "ONBOARDING_INCOMPLETE");


        Guid dotNetSkillId;
        Guid postgresSkillId;
        Guid backendRoleId;

        using (var catalogScope = _factory.Services.CreateScope())
        {
            var catalogDb = catalogScope.ServiceProvider
                .GetRequiredService<TindyDbContext>();

            // Get Skill IDs
            dotNetSkillId = await catalogDb.Skills
                .Where(x => x.NameNormalized == ".net")
                .Select(x => x.SkillId)
                .SingleAsync();

            postgresSkillId = await catalogDb.Skills
                .Where(x => x.NameNormalized == "postgresql")
                .Select(x => x.SkillId)
                .SingleAsync();

            // Get Professional Role ID
            backendRoleId = await catalogDb.ProfessionalRoles
                .Where(x => x.NameNormalized == "backend developer")
                .Select(x => x.ProfessionalRoleId)
                .SingleAsync();
        }


        var steps = new (int Number, object Body)[]
        {
            (1, new { username, display_name = "Step 14 Test", university = "FPT University" }),
            (2, new { skill_ids = new[] { dotNetSkillId, postgresSkillId, dotNetSkillId } }),
            (3, new { preferred_role_id = backendRoleId }),
            (4, new { professional_interests = "Cloud", learning_goals = "Build useful APIs" }),
            (5, new { available_hours_per_week = 10 }),
            (6, new { previous_projects_and_contribution = "Built an API", technologies_to_learn = "Docker" }),
            (7, new { github_url = (string?)null, personal_website_url = (string?)null })
        };

        foreach (var (number, body) in steps)
        {
            var put = await client.PutAsJsonAsync($"/api/me/onboarding/{number}", body);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        // Step 1 username is also accepted for local login.
        var usernameLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = username.ToUpperInvariant(),
            password
        });
        Assert.Equal(HttpStatusCode.OK, usernameLogin.StatusCode);

        const string bio = "My own summary; AI suggestions are optional.";
        var finish = await client.PostAsJsonAsync("/api/me/onboarding/complete", new
        {
            confirmed = true,
            bio
        });
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        using var finishJson = await ReadJsonAsync(finish);
        Assert.True(finishJson.RootElement.GetProperty("data").GetProperty("is_onboarding_completed").GetBoolean());

        // STEP 13: SAME JWT should work immediately after completion.
        var gateAfter = await client.GetAsync("/api/dev/onboarding-gate-check");
        Assert.Equal(HttpStatusCode.OK, gateAfter.StatusCode);

        // STEP 10: Logout response is stateless, it does not revoke the JWT.
        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dev/onboarding-gate-check")).StatusCode);

        // Database assertions use a new EF scope, not the controller's tracking scope.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var stored = await db.Users.AsNoTracking()
            .Include(x => x.Credential)
            .Include(x => x.Profile)!.ThenInclude(p => p!.UserSkills)
            .Include(x => x.OnboardingSteps)
            .SingleAsync(x => x.Credential != null && x.Credential.EmailNormalized == email);

        Assert.True(stored.IsOnboardingCompleted);
        Assert.Equal(username, stored.Username);
        Assert.Equal(bio, stored.Profile!.Bio);
        Assert.Equal(backendRoleId, stored.Profile.PreferredRoleId);
        Assert.Equal(7, stored.OnboardingSteps.Count);
        Assert.Equal(2, stored.Profile.UserSkills.Count); // duplicates removed
        Assert.Single(await db.UserCredentials.Where(x => x.EmailNormalized == email).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRegistration_SameEmail_CreatesExactlyOneUser()
    {
        using var client = _factory.CreateHttpsClient();
        var email = $"race.{Guid.NewGuid():N}@example.com";
        var body1 = new
        {
            display_name = "User One",
            university = "FPT University",
            email,
            password = "Password123!",
            accept_terms = true
        };
        var body2 = new
        {
            display_name = "User Two",
            university = "FPT University",
            email = email.ToUpperInvariant(),
            password = "Password123!",
            accept_terms = true
        };

        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/auth/register", body1), client.PostAsJsonAsync("/api/auth/register", body2));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(1, await db.UserCredentials.CountAsync(x => x.EmailNormalized == email));
    }

    [Fact]
    public async Task DisabledAccount_IsRejectedByBusinessGate_WithUnexpiredJwt()
    {
        using var client = _factory.CreateHttpsClient();
        var email = $"disabled.{Guid.NewGuid():N}@example.com";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Blocked User",
            university = "FPT University",
            email,
            password = "Password123!",
            accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var body = await ReadJsonAsync(registration);
        var token = body.RootElement.GetProperty("data").GetProperty("access_token").GetString();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            var user = await db.Users.SingleAsync(x => x.Credential != null && x.Credential.EmailNormalized == email);
            user.Status = "suspended";
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var gated = await client.GetAsync("/api/dev/onboarding-gate-check");
        Assert.Equal(HttpStatusCode.Forbidden, gated.StatusCode);
    }

    [Fact]
    public async Task PublicAndProtectedEndpoints_EnforceHttpContracts()
    {
        using var client = _factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dev/onboarding-gate-check")).StatusCode);

        var invalid = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "",
            university = "",
            email = "not-an-email",
            password = "weak",
            accept_terms = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await AssertErrorAsync(invalid, "VALIDATION_FAILED");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        using var body = await ReadJsonAsync(response);
        Assert.Equal(code, body.RootElement.GetProperty("error_code").GetString());
    }
}
