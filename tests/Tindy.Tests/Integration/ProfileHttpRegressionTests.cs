using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

/// <summary>
/// STEP 11: REAL HTTP/JWT/PostgreSQL checks. Requires Docker/Testcontainers.
/// Do not run as part of the no-Docker unit-test filter.
/// </summary>
public sealed class ProfileHttpRegressionTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public ProfileHttpRegressionTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AnonymousAndIncompleteAccounts_CannotAccessProfileCollections()
    {
        using var anonymous = _factory.CreateHttpsClient();
        foreach (var path in new[] { "/api/me/profile", "/api/me/profile/experiences", "/api/me/profile/certificates" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        using var incomplete = await RegisterAsync("incomplete");
        foreach (var path in new[] { "/api/me/profile", "/api/me/profile/experiences", "/api/me/profile/certificates" })
            Assert.Equal(HttpStatusCode.Forbidden, (await incomplete.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task CompletedMembers_CrudIsolationValidationAndSafeResponse_EndToEnd()
    {
        using var owner = await RegisterAsync("owner");
        using var another = await RegisterAsync("other");
        var (skillId, roleId) = await SeededCatalogIdsAsync();
        await CompleteOnboardingAsync(owner, skillId, roleId);
        await CompleteOnboardingAsync(another, skillId, roleId);

        var selfResponse = await owner.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.OK, selfResponse.StatusCode);
        using var self = await ReadJsonAsync(selfResponse);
        var ownerId = self.RootElement.GetProperty("data").GetProperty("user_id").GetGuid();
        Assert.Equal(0, self.RootElement.GetProperty("data").GetProperty("experiences_count").GetInt32());
        Assert.Equal(0, self.RootElement.GetProperty("data").GetProperty("certificates_count").GetInt32());

        // Presence-aware PATCH: null clears and unknown Account fields are rejected.
        var patch = await owner.PatchAsJsonAsync("/api/me/profile", new { bio = (string?)null });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var takeover = await owner.PatchAsJsonAsync("/api/me/profile", new { is_onboarding_completed = false });
        Assert.Equal(HttpStatusCode.BadRequest, takeover.StatusCode);
        await AssertErrorAsync(takeover, "VALIDATION_FAILED");
        var locked = await owner.PutAsJsonAsync("/api/me/onboarding/4", new { professional_interests = "tamper" });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        await AssertErrorAsync(locked, "ONBOARDING_ALREADY_COMPLETED");

        var experienceBody = new
        {
            project_name = "RAG demo", role_title = "Backend Developer", description = "Created .NET API",
            repo_url = (string?)null, start_date = "2026-09", end_date = (string?)null,
            is_ongoing = true, skill_ids = new[] { skillId }
        };
        var createdExperience = await owner.PostAsJsonAsync("/api/me/profile/experiences", experienceBody);
        Assert.Equal(HttpStatusCode.Created, createdExperience.StatusCode);
        using var experienceJson = await ReadJsonAsync(createdExperience);
        var experienceId = experienceJson.RootElement.GetProperty("data").GetProperty("experience_id").GetGuid();
        Assert.Equal("2026-09", experienceJson.RootElement.GetProperty("data").GetProperty("start_date").GetString());

        var certificateBody = new
        {
            title = "AWS Badge", issuer = "AWS", description = "Foundational AWS knowledge.",
            credential_url = "https://www.credly.com/badges/test"
        };
        var createdCertificate = await owner.PostAsJsonAsync("/api/me/profile/certificates", certificateBody);
        Assert.Equal(HttpStatusCode.Created, createdCertificate.StatusCode);
        using var certificateJson = await ReadJsonAsync(createdCertificate);
        var certificateId = certificateJson.RootElement.GetProperty("data").GetProperty("certificate_id").GetGuid();

        var otherCannotPutExperience = await another.PutAsJsonAsync($"/api/me/profile/experiences/{experienceId}", experienceBody);
        Assert.Equal(HttpStatusCode.NotFound, otherCannotPutExperience.StatusCode);
        var otherCannotDeleteExperience = await another.DeleteAsync($"/api/me/profile/experiences/{experienceId}");
        Assert.Equal(HttpStatusCode.NotFound, otherCannotDeleteExperience.StatusCode);
        var otherCannotPutCertificate = await another.PutAsJsonAsync($"/api/me/profile/certificates/{certificateId}", certificateBody);
        Assert.Equal(HttpStatusCode.NotFound, otherCannotPutCertificate.StatusCode);
        var otherCannotDeleteCertificate = await another.DeleteAsync($"/api/me/profile/certificates/{certificateId}");
        Assert.Equal(HttpStatusCode.NotFound, otherCannotDeleteCertificate.StatusCode);

        var member = await another.GetAsync($"/api/users/{ownerId}/profile");
        Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        using (var memberJson = await ReadJsonAsync(member))
        {
            var data = memberJson.RootElement.GetProperty("data");
            foreach (var field in new[] { "phone_number", "resume_url", "primary_email", "password_hash", "external_auth_accounts" })
                Assert.False(data.TryGetProperty(field, out _), $"Leaked: {field}");
            Assert.Equal(1, data.GetProperty("experiences_count").GetInt32());
            Assert.Equal(1, data.GetProperty("certificates_count").GetInt32());
        }
        Assert.Equal(HttpStatusCode.OK, (await another.GetAsync($"/api/users/{ownerId}/profile/experiences")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await another.GetAsync($"/api/users/{ownerId}/profile/certificates")).StatusCode);

        var invalidTimeline = await owner.PostAsJsonAsync("/api/me/profile/experiences", new
        {
            project_name = "Invalid", role_title = "Role", description = "Description",
            start_date = "2026-10", end_date = "2026-09", is_ongoing = false, skill_ids = new[] { skillId }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidTimeline.StatusCode);
        await AssertErrorAsync(invalidTimeline, "VALIDATION_FAILED");
        var invalidCertificate = await owner.PostAsJsonAsync("/api/me/profile/certificates", new
        {
            title = "Invalid", issuer = "AWS", description = new string('x', 2001)
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCertificate.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/me/profile/experiences/{experienceId}", experienceBody)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/me/profile/certificates/{certificateId}", certificateBody)).StatusCode);
        var delExperience = await owner.DeleteAsync($"/api/me/profile/experiences/{experienceId}");
        Assert.Equal(HttpStatusCode.NoContent, delExperience.StatusCode);
        Assert.Equal(string.Empty, await delExperience.Content.ReadAsStringAsync());
        var delCertificate = await owner.DeleteAsync($"/api/me/profile/certificates/{certificateId}");
        Assert.Equal(HttpStatusCode.NoContent, delCertificate.StatusCode);

        using var emptyExperiences = await ReadJsonAsync(await owner.GetAsync("/api/me/profile/experiences"));
        using var emptyCertificates = await ReadJsonAsync(await owner.GetAsync("/api/me/profile/certificates"));
        Assert.Empty(emptyExperiences.RootElement.GetProperty("data").EnumerateArray());
        Assert.Empty(emptyCertificates.RootElement.GetProperty("data").EnumerateArray());
    }

    private async Task<HttpClient> RegisterAsync(string tag)
    {
        var client = _factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = $"Profile {tag}", university = "FPT University",
            email = $"profile11.{tag}.{Guid.NewGuid():N}@example.com", password = "Password123!", accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var token = json.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid Skill, Guid Role)> SeededCatalogIdsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var skill = await db.Skills.Where(s => s.NameNormalized == ".net").Select(s => s.SkillId).SingleAsync();
        var role = await db.ProfessionalRoles.Where(r => r.NameNormalized == "backend developer")
            .Select(r => r.ProfessionalRoleId).SingleAsync();
        return (skill, role);
    }

    private static async Task CompleteOnboardingAsync(HttpClient client, Guid skillId, Guid roleId)
    {
        var username = "p11" + Guid.NewGuid().ToString("N")[..18];
        var steps = new (int Number, object Body)[]
        {
            (1, new { username, display_name = "Profile Test", university = "FPT University" }),
            (2, new { skill_ids = new[] { skillId } }),
            (3, new { preferred_role_id = roleId }),
            (4, new { professional_interests = "Cloud", learning_goals = (string?)null }),
            (5, new { available_hours_per_week = 10 }),
            (6, new { previous_projects_and_contribution = (string?)null, technologies_to_learn = (string?)null }),
            (7, new { github_url = (string?)null, personal_website_url = (string?)null })
        };
        foreach (var (number, body) in steps)
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/me/onboarding/{number}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true })).StatusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertErrorAsync(HttpResponseMessage response, string expectedErrorCode)
    {
        using var json = await ReadJsonAsync(response);
        Assert.Equal(expectedErrorCode, json.RootElement.GetProperty("error_code").GetString());
    }
}
