using System.Data;
using System.Net;
using System.Net.Http.Json;
using Tindy.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Xunit;

namespace Tindy.Tests.Integration;

/// <summary>Verifies the actual schema and idempotent catalog seeding in a throwaway DB.</summary>
public sealed class CatalogDatabaseIntegrityTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public CatalogDatabaseIntegrityTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AppliedMigrations_IncludeCatalogAndPreferredRoleConversion()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains(applied, x => x.EndsWith("_Catalog_CreateProfessionalRolesAndSkillCategories", StringComparison.Ordinal));
        Assert.Contains(applied, x => x.EndsWith("_Profile_ConvertPreferredRoleToReference", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Schema_HasExpectedTablesIncludingMedia_AndForeignKeyColumns()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            """;
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        var expected = new[]
        {
            "users", "user_profiles", "roles", "permissions", "user_roles", "role_permissions",
            "user_credentials", "external_auth_accounts", "professional_roles", "skill_categories",
            "skills", "skill_category_skills", "user_skills", "user_onboarding_steps",
            "profile_experiences", "profile_experience_skills", "user_certificates",
            "oauth_login_tickets", "oauth_link_transactions", "media_assets", "user_certificate_attachments"
        };
        Assert.True(expected.ToHashSet().SetEquals(tables),
            $"Expected {expected.Length} business tables. Actual: {string.Join(", ", tables.OrderBy(x => x))}");

        command.CommandText = """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND ((table_name='user_profiles' AND column_name IN ('preferred_role_id','preferred_role'))
                OR (table_name='skills' AND column_name='is_active'))
            """;
        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        }
        Assert.Contains("user_profiles.preferred_role_id", columns);
        Assert.Contains("skills.is_active", columns);
        Assert.DoesNotContain("user_profiles.preferred_role", columns);

        command.CommandText = """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'user_profiles'
              AND column_name = 'is_public'
            """;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.False(await reader.ReadAsync());
        }

        command.CommandText = """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND ((table_name = 'profile_experiences' AND column_name IN ('experience_id', 'user_id', 'start_date', 'end_date'))
                OR (table_name = 'profile_experience_skills' AND column_name IN ('experience_id', 'skill_id'))
                OR (table_name = 'user_certificates' AND column_name IN ('certificate_id', 'user_id', 'description', 'credential_url')))
            """;
        var profileColumns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) profileColumns.Add(reader.GetString(0));
        }
        Assert.Equal(10, profileColumns.Count);
        Assert.Contains("user_certificates.description", profileColumns);
    }

    [Fact]
    public async Task SeedingTwice_DoesNotChangeCatalogCounts_OrExistingSkillIds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var before = await SnapshotAsync(db);
        Assert.True(before.Roles >= 11 && before.Categories >= 7 && before.Skills >= 49 && before.Links >= 54);
        var postgresIdBefore = await db.Skills.AsNoTracking()
            .Where(x => x.NameNormalized == "postgresql").Select(x => x.SkillId).SingleAsync();

        await TindySeedRunner.SeedAsync(db);
        db.ChangeTracker.Clear();
        await TindySeedRunner.SeedAsync(db);
        db.ChangeTracker.Clear();
        Assert.Equal(before, await SnapshotAsync(db));
        var postgresIdAfter = await db.Skills.AsNoTracking()
            .Where(x => x.NameNormalized == "postgresql").Select(x => x.SkillId).SingleAsync();
        Assert.Equal(postgresIdBefore, postgresIdAfter);
    }

    [Fact]
    public async Task SeedingAgain_PreservesExistingUserSkillLink()
    {
        using var client = _factory.CreateHttpsClient();
        var email = $"seed-preserve.{Guid.NewGuid():N}@example.com";
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            display_name = "Existing User", university = "FPT University",
            email, password = "Password123!", accept_terms = true
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var userId = await db.UserCredentials.AsNoTracking()
            .Where(x => x.EmailNormalized == email.ToLowerInvariant())
            .Select(x => x.UserId).SingleAsync();
        var skillId = await db.Skills.AsNoTracking()
            .Where(x => x.NameNormalized == "postgresql")
            .Select(x => x.SkillId).SingleAsync();

        db.UserSkills.Add(new UserSkill { UserId = userId, SkillId = skillId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await TindySeedRunner.SeedAsync(db);
        db.ChangeTracker.Clear();
        Assert.True(await db.UserSkills.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.SkillId == skillId));
    }

    private static async Task<(int Roles, int Categories, int Skills, int Links)> SnapshotAsync(TindyDbContext db) =>
        (await db.ProfessionalRoles.CountAsync(),
         await db.SkillCategories.CountAsync(),
         await db.Skills.CountAsync(),
         await db.SkillCategorySkills.CountAsync());
}
