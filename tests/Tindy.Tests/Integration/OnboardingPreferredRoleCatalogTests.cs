using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Application.Entities;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class OnboardingPreferredRoleCatalogTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;

    public OnboardingPreferredRoleCatalogTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Step3_ValidRole_StoresForeignKey_AndReturnsDraft()
    {
        using var client = await RegisterAuthorizedClientAsync();
        Guid backendId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            backendId = await db.ProfessionalRoles.Where(x => x.NameNormalized == "backend developer")
                .Select(x => x.ProfessionalRoleId).SingleAsync();
        }

        var save = await client.PutAsJsonAsync("/api/me/onboarding/3", new { preferred_role_id = backendId });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var draft = await client.GetAsync("/api/me/onboarding");
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        using var json = JsonDocument.Parse(await draft.Content.ReadAsStringAsync());
        var step3 = json.RootElement.GetProperty("data").GetProperty("step_data").GetProperty("3");
        Assert.Equal(backendId, step3.GetProperty("preferred_role_id").GetGuid());
        Assert.Equal("Backend Developer", step3.GetProperty("preferred_role").GetString());
        Assert.Contains(3, json.RootElement.GetProperty("data").GetProperty("completed_steps")
            .EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task Step3_UnknownOrInactiveRole_DoesNotOverwriteExistingSelection()
    {
        using var client = await RegisterAuthorizedClientAsync();
        Guid backendId;
        Guid inactiveId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            backendId = await db.ProfessionalRoles.Where(x => x.NameNormalized == "backend developer")
                .Select(x => x.ProfessionalRoleId).SingleAsync();
            inactiveId = Guid.NewGuid();
            var name = "Temporarily Inactive Role " + inactiveId.ToString("N");
            db.ProfessionalRoles.Add(new ProfessionalRole
            {
                ProfessionalRoleId = inactiveId,
                Name = name,
                NameNormalized = name.ToLowerInvariant(),
                IsActive = true
            });
            await db.SaveChangesAsync();
            // Force false to avoid an EF default-TRUE insert ambiguity.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE professional_roles SET is_active = FALSE WHERE professional_role_id = {inactiveId}");
        }

        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/me/onboarding/3", new { preferred_role_id = backendId })).StatusCode);

        var unknown = await client.PutAsJsonAsync("/api/me/onboarding/3", new { preferred_role_id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        await AssertErrorAsync(unknown, "BUSINESS_RULE_VIOLATION");

        var inactive = await client.PutAsJsonAsync("/api/me/onboarding/3", new { preferred_role_id = inactiveId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactive.StatusCode);
        await AssertErrorAsync(inactive, "BUSINESS_RULE_VIOLATION");

        var draft = await client.GetAsync("/api/me/onboarding");
        using var json = JsonDocument.Parse(await draft.Content.ReadAsStringAsync());
        Assert.Equal(backendId, json.RootElement.GetProperty("data").GetProperty("step_data")
            .GetProperty("3").GetProperty("preferred_role_id").GetGuid());
    }

    [Fact]
    public async Task Step3_RejectsMissingEmptyAndLegacyStringPayloads()
    {
        using var client = await RegisterAuthorizedClientAsync();
        foreach (var payload in new object[]
        {
            new { preferred_role_id = (Guid?)null },
            new { preferred_role_id = Guid.Empty },
            new { preferred_role = "Backend Developer" }
        })
        {
            var response = await client.PutAsJsonAsync("/api/me/onboarding/3", payload);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertErrorAsync(response, "VALIDATION_FAILED");
        }
    }

    private async Task<HttpClient> RegisterAuthorizedClientAsync()
    {
        var client = _factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Role Test", university = "FPT University",
            email = $"step3.{Guid.NewGuid():N}@example.com",
            password = "Password123!", accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string errorCode)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(errorCode, json.RootElement.GetProperty("error_code").GetString());
    }
}
