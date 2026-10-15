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

/// <summary>Read-only Catalog API contract, backed by an isolated Testcontainers PostgreSQL DB.</summary>
public sealed class CatalogReadApiTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public CatalogReadApiTests(TindyApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/catalog/skill-categories")]
    [InlineData("/api/catalog/skills")]
    [InlineData("/api/catalog/professional-roles")]
    public async Task WithoutJwt_CatalogEndpoints_Return401(string url)
    {
        using var client = _factory.CreateHttpsClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PendingOnboardingUser_CanReadActiveCatalog_WithSnakeCase_AndUniqueSkills()
    {
        using var client = await RegisterAuthorizedClientAsync();

        using var categories = await ReadDataAsync(client, "/api/catalog/skill-categories");
        var categoryItems = categories.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.True(categoryItems.Length >= 7);
        Assert.All(categoryItems, item =>
        {
            Assert.True(item.TryGetProperty("category_id", out _));
            Assert.True(item.TryGetProperty("display_order", out _));
            Assert.True(item.TryGetProperty("name", out _));
        });

        using var skills = await ReadDataAsync(client, "/api/catalog/skills");
        var skillItems = skills.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.True(skillItems.Length >= 49);
        Assert.All(skillItems, item =>
        {
            Assert.True(item.TryGetProperty("skill_id", out _));
            Assert.True(item.TryGetProperty("name", out _));
        });
        Assert.Equal(skillItems.Length, skillItems.Select(x => x.GetProperty("skill_id").GetGuid()).Distinct().Count());

        using var roles = await ReadDataAsync(client, "/api/catalog/professional-roles");
        var roleItems = roles.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.True(roleItems.Length >= 11);
        Assert.All(roleItems, item => Assert.True(item.TryGetProperty("professional_role_id", out _)));
    }

    [Fact]
    public async Task CategoryFilter_AndCaseInsensitiveSearch_ReturnMatchingSkills()
    {
        using var client = await RegisterAuthorizedClientAsync();
        using var categories = await ReadDataAsync(client, "/api/catalog/skill-categories");
        var category = categories.RootElement.GetProperty("data").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "Backend Development");
        var id = category.GetProperty("category_id").GetGuid();

        using var filtered = await ReadDataAsync(client, $"/api/catalog/skills?category_id={id}&search=%20%20pYtHoN%20%20");
        var items = filtered.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("Python", items[0].GetProperty("name").GetString());

        using var roles = await ReadDataAsync(client, "/api/catalog/professional-roles?search=%20%20BACKEND%20%20");
        Assert.Contains(roles.RootElement.GetProperty("data").EnumerateArray(),
            x => x.GetProperty("name").GetString() == "Backend Developer");
    }

    [Fact]
    public async Task InvalidOrUnknownCategory_Returns400OrEmptyList()
    {
        using var client = await RegisterAuthorizedClientAsync();
        var invalid = await client.GetAsync("/api/catalog/skills?category_id=not-a-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var unknown = await ReadDataAsync(client, $"/api/catalog/skills?category_id={Guid.NewGuid()}");
        Assert.Empty(unknown.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task InactiveCatalogEntries_AreHidden_AndCategoryInactiveFiltersNoSkills()
    {
        using var client = await RegisterAuthorizedClientAsync();
        var categoryId = Guid.NewGuid();
        var skillId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");
        var skillName = "CatalogOnlySkill" + suffix;
        var roleName = "CatalogOnlyRole" + suffix;
        var categoryName = "CatalogOnlyCategory" + suffix;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            db.SkillCategories.Add(new SkillCategory { CategoryId = categoryId, Name = categoryName, NameNormalized = categoryName.ToLowerInvariant(), IsActive = true });
            db.Skills.Add(new Skill { SkillId = skillId, Name = skillName, NameNormalized = skillName.ToLowerInvariant(), IsActive = true });
            db.ProfessionalRoles.Add(new ProfessionalRole { ProfessionalRoleId = roleId, Name = roleName, NameNormalized = roleName.ToLowerInvariant(), IsActive = true });
            db.SkillCategorySkills.Add(new SkillCategorySkill { CategoryId = categoryId, SkillId = skillId });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE skill_categories SET is_active = FALSE WHERE category_id = {categoryId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE professional_roles SET is_active = FALSE WHERE professional_role_id = {roleId}");
        }

        using var categories = await ReadDataAsync(client, "/api/catalog/skill-categories");
        Assert.DoesNotContain(categories.RootElement.GetProperty("data").EnumerateArray(),
            x => x.GetProperty("category_id").GetGuid() == categoryId);
        using var roles = await ReadDataAsync(client, $"/api/catalog/professional-roles?search={roleName}");
        Assert.Empty(roles.RootElement.GetProperty("data").EnumerateArray());
        using var filterByInactiveCategory = await ReadDataAsync(client, $"/api/catalog/skills?category_id={categoryId}");
        Assert.Empty(filterByInactiveCategory.RootElement.GetProperty("data").EnumerateArray());

        // A skill itself can also become inactive, independently of its category.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE skills SET is_active = FALSE WHERE skill_id = {skillId}");
        }
        using var skillSearch = await ReadDataAsync(client, $"/api/catalog/skills?search={skillName}");
        Assert.Empty(skillSearch.RootElement.GetProperty("data").EnumerateArray());
    }

    private async Task<HttpClient> RegisterAuthorizedClientAsync()
    {
        var client = _factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Catalog Reader", university = "FPT University",
            email = $"catalog-read.{Guid.NewGuid():N}@example.com",
            password = "Password123!", accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> ReadDataAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
