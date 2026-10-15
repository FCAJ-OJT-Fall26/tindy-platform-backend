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

public sealed class OnboardingCompletionRulesTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;

    public OnboardingCompletionRulesTests(TindyApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Complete_RejectsMissingSkillOrPreferredRoleStep(int missingStep)
    {
        var account = await RegisterAsync();
        using var client = account.Client;
        var (skillId, roleId) = await GetCatalogIdsAsync();

        await SaveStepsAsync(client, skillId, roleId, skipStep: missingStep);

        var result = await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.StatusCode);
        using (var json = await ReadJsonAsync(result))
        {
            Assert.Equal("ONBOARDING_INCOMPLETE", json.RootElement.GetProperty("error_code").GetString());
            Assert.Equal(new[] { missingStep }, json.RootElement.GetProperty("missing_steps")
                .EnumerateArray().Select(x => x.GetInt32()).ToArray());
        }

        using var draft = await ReadJsonAsync(await client.GetAsync("/api/me/onboarding"));
        var progress = draft.RootElement.GetProperty("data");
        Assert.False(progress.GetProperty("is_onboarding_completed").GetBoolean());
        Assert.Equal(missingStep, progress.GetProperty("current_step").GetInt32());
        Assert.DoesNotContain(missingStep, progress.GetProperty("completed_steps")
            .EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task Step2_SaveResponseCountsNewSkillWithoutLoadedNavigation()
    {
        var account = await RegisterAsync();
        using var client = account.Client;
        var (skillId, _) = await GetCatalogIdsAsync();

        var response = await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { skillId } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var completed = json.RootElement.GetProperty("data").GetProperty("completed_steps")
            .EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Contains(2, completed);
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("next_step").GetInt32());
    }

    [Fact]
    public async Task Step3_SaveResponseCountsRoleImmediatelyWithoutReloadingEntity()
    {
        var account = await RegisterAsync();
        using var client = account.Client;
        var (_, roleId) = await GetCatalogIdsAsync();

        var response = await client.PutAsJsonAsync("/api/me/onboarding/3", new { preferred_role_id = roleId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var completed = json.RootElement.GetProperty("data").GetProperty("completed_steps")
            .EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Contains(3, completed);
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("next_step").GetInt32());
    }

    [Fact]
    public async Task Complete_RequiresConfirmation_AllowsNullBio_AndIsIdempotent()
    {
        var account = await RegisterAsync();
        using var client = account.Client;
        var (skillId, roleId) = await GetCatalogIdsAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/dev/onboarding-gate-check")).StatusCode);
        await SaveStepsAsync(client, skillId, roleId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/dev/onboarding-gate-check")).StatusCode);

        var unconfirmed = await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = false });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        var first = await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var json = await ReadJsonAsync(first))
        {
            var data = json.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("is_onboarding_completed").GetBoolean());
            Assert.Equal(Enumerable.Range(1, 8), data.GetProperty("completed_steps")
                .EnumerateArray().Select(x => x.GetInt32()).ToArray());
        }

        var second = await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/api/dev/onboarding-gate-check")).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var stored = await db.Users.AsNoTracking()
            .Include(x => x.Profile)
            .Include(x => x.OnboardingSteps)
            .SingleAsync(x => x.Credential != null && x.Credential.EmailNormalized == account.Email);
        Assert.True(stored.IsOnboardingCompleted);
        Assert.Null(stored.Profile!.Bio);
        Assert.Equal(Enumerable.Range(1, 7), stored.OnboardingSteps
            .Select(x => x.StepNumber).OrderBy(x => x));
        Assert.DoesNotContain(stored.OnboardingSteps, x => x.StepNumber == 8);
    }

    [Fact]
    public async Task Complete_RejectsStaleStep2Marker_WhenUserSkillsAreMissing()
    {
        var account = await RegisterAsync();
        using var client = account.Client;
        var (skillId, roleId) = await GetCatalogIdsAsync();
        await SaveStepsAsync(client, skillId, roleId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            var userId = await db.Users.Where(x => x.Credential != null && x.Credential.EmailNormalized == account.Email)
                .Select(x => x.UserId).SingleAsync();
            db.UserSkills.RemoveRange(await db.UserSkills.Where(x => x.UserId == userId).ToListAsync());
            await db.SaveChangesAsync();
            Assert.True(await db.UserOnboardingSteps.AnyAsync(x => x.UserId == userId && x.StepNumber == 2));
        }

        var response = await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using (var json = await ReadJsonAsync(response))
        {
            Assert.Equal(new[] { 2 }, json.RootElement.GetProperty("missing_steps")
                .EnumerateArray().Select(x => x.GetInt32()).ToArray());
        }

        using (var draft = await ReadJsonAsync(await client.GetAsync("/api/me/onboarding")))
        {
            var data = draft.RootElement.GetProperty("data");
            Assert.Equal(2, data.GetProperty("current_step").GetInt32());
            Assert.DoesNotContain(2, data.GetProperty("completed_steps")
                .EnumerateArray().Select(x => x.GetInt32()));
        }

        // Restore the missing data through the supported API; the old marker is reused.
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/me/onboarding/2", new { skill_ids = new[] { skillId } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true })).StatusCode);
    }

    [Fact]
    public async Task Complete_AcceptsAlreadySelectedRole_WhenRoleIsLaterDisabled()
    {
        var account = await RegisterAsync();
        using var client = account.Client;
        var (skillId, _) = await GetCatalogIdsAsync();
        var roleId = Guid.NewGuid();
        var name = "Archive Role " + roleId.ToString("N");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.ProfessionalRoles.Add(new ProfessionalRole
            {
                ProfessionalRoleId = roleId,
                Name = name,
                NameNormalized = name.ToLowerInvariant(),
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await SaveStepsAsync(client, skillId, roleId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE professional_roles SET is_active = FALSE WHERE professional_role_id = {roleId}");
        }

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/me/onboarding/complete", new { confirmed = true })).StatusCode);
    }

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var client = _factory.CreateHttpsClient();
        var email = $"complete.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Onboarding Completion Test",
            university = "FPT University",
            email,
            password = "Password123!",
            accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var token = json.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, email);
    }

    private async Task<(Guid SkillId, Guid RoleId)> GetCatalogIdsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var skillId = await db.Skills.Where(x => x.NameNormalized == ".net")
            .Select(x => x.SkillId).SingleAsync();
        var roleId = await db.ProfessionalRoles.Where(x => x.NameNormalized == "backend developer")
            .Select(x => x.ProfessionalRoleId).SingleAsync();
        return (skillId, roleId);
    }

    private static async Task SaveStepsAsync(HttpClient client, Guid skillId, Guid roleId, int skipStep = 0)
    {
        var username = "complete" + Guid.NewGuid().ToString("N")[..14];
        var steps = new (int Step, object Body)[]
        {
            (1, new { username, display_name = "Completion Test", university = "FPT University" }),
            (2, new { skill_ids = new[] { skillId } }),
            (3, new { preferred_role_id = roleId }),
            (4, new { professional_interests = "Backend", learning_goals = "Build services" }),
            (5, new { available_hours_per_week = 10 }),
            (6, new { previous_projects_and_contribution = "Built an API", technologies_to_learn = "Docker" }),
            (7, new { github_url = (string?)null, personal_website_url = (string?)null })
        };

        foreach (var (step, body) in steps)
        {
            if (step == skipStep)
                continue;

            var response = await client.PutAsJsonAsync($"/api/me/onboarding/{step}", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
