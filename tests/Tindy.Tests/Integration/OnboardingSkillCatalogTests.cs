using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class OnboardingSkillCatalogTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;

    public OnboardingSkillCatalogTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Step2_UsesCatalogIds_RemovesDuplicates_ReplacesSelection_AndRestoresDraft()
    {
        using var client = await RegisterAuthorizedClientAsync();
        Guid first;
        Guid second;
        int catalogCountBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            first = await db.Skills.Where(x => x.NameNormalized == ".net")
                .Select(x => x.SkillId).SingleAsync();
            second = await db.Skills.Where(x => x.NameNormalized == "postgresql")
                .Select(x => x.SkillId).SingleAsync();
            catalogCountBefore = await db.Skills.CountAsync();
        }

        var initial = await client.PutAsJsonAsync("/api/me/onboarding/2", new
        {
            skill_ids = new[] { first, second, first }
        });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        var draftResponse = await client.GetAsync("/api/me/onboarding");
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        using (var draft = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync()))
        {
            var step2 = draft.RootElement.GetProperty("data").GetProperty("step_data").GetProperty("2");
            var ids = step2.GetProperty("skill_ids").EnumerateArray().Select(x => x.GetGuid()).ToHashSet();
            Assert.True(ids.SetEquals([first, second]));
            Assert.All(step2.GetProperty("skills").EnumerateArray(), skill =>
            {
                Assert.True(skill.TryGetProperty("skill_id", out _));
                Assert.True(skill.TryGetProperty("name", out _));
            });
        }

        var replace = await client.PutAsJsonAsync("/api/me/onboarding/2", new
        {
            skill_ids = new[] { second }
        });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<TindyDbContext>();
        // This onboarding endpoint must never create new catalog skills.
        Assert.Equal(catalogCountBefore, await dbAfter.Skills.CountAsync());
        var userId = await dbAfter.Users
            .Where(x => x.Credential != null && x.Credential.EmailNormalized == _currentEmail)
            .Select(x => x.UserId).SingleAsync();
        var chosen = await dbAfter.UserSkills.Where(x => x.UserId == userId)
            .Select(x => x.SkillId).ToArrayAsync();
        Assert.Equal(new[] { second }, chosen);
        Assert.Single(await dbAfter.UserOnboardingSteps.Where(x => x.UserId == userId && x.StepNumber == 2).ToListAsync());
    }

    [Fact]
    public async Task Step2_Rejects_InvalidPayload_WithoutCreatingCatalogSkills()
    {
        using var client = await RegisterAuthorizedClientAsync();
        var initialCount = await CountSkillsAsync();

        var empty = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        await AssertErrorCodeAsync(empty, "VALIDATION_FAILED");

        var emptyId = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { Guid.Empty } });
        Assert.Equal(HttpStatusCode.BadRequest, emptyId.StatusCode);
        await AssertErrorCodeAsync(emptyId, "VALIDATION_FAILED");

        var badFormat = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { "invalid-guid" } });
        Assert.Equal(HttpStatusCode.BadRequest, badFormat.StatusCode);
        await AssertErrorCodeAsync(badFormat, "VALIDATION_FAILED");

        var legacyText = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skills = new[] { "UnknownSkill" } });
        Assert.Equal(HttpStatusCode.BadRequest, legacyText.StatusCode);

        Assert.Equal(initialCount, await CountSkillsAsync());
    }

    [Fact]
    public async Task Step2_UnknownOrInactiveSkill_RollsBack_AndKeepsPreviousSelection()
    {
        using var client = await RegisterAuthorizedClientAsync();
        Guid activeId;
        var inactiveId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            activeId = await db.Skills.Where(x => x.NameNormalized == ".net")
                .Select(x => x.SkillId).SingleAsync();
            var inactiveName = "InactiveTestSkill_" + Guid.NewGuid().ToString("N");
            db.Skills.Add(new Skill
            {
                SkillId = inactiveId,
                Name = inactiveName,
                NameNormalized = inactiveName.ToLowerInvariant(),
                IsActive = true
            });
            await db.SaveChangesAsync();
            // Explicit SQL update avoids any ambiguity around a database default of TRUE.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE skills SET is_active = FALSE WHERE skill_id = {inactiveId}");
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { activeId } })).StatusCode);

        var unknown = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        await AssertErrorCodeAsync(unknown, "BUSINESS_RULE_VIOLATION");

        var inactive = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { inactiveId } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactive.StatusCode);
        await AssertErrorCodeAsync(inactive, "BUSINESS_RULE_VIOLATION");

        var draftResponse = await client.GetAsync("/api/me/onboarding");
        using var draft = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var ids = draft.RootElement.GetProperty("data").GetProperty("step_data")
            .GetProperty("2").GetProperty("skill_ids").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        Assert.Equal(new[] { activeId }, ids);
    }

    private string _currentEmail = "";

    private async Task<HttpClient> RegisterAuthorizedClientAsync()
    {
        var client = _factory.CreateHttpsClient();
        _currentEmail = $"catalog-step2.{Guid.NewGuid():N}@example.com";
        var result = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Catalog Test", university = "FPT University",
            email = _currentEmail, password = "Password123!", accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<int> CountSkillsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        return await db.Skills.CountAsync();
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string code)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("error_code").GetString());
    }
}
