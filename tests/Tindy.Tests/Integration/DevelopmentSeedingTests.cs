using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Tindy.Infrastructure.Services;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class DevelopmentSeedOptionsTests
{
    [Fact]
    public void DisabledByDefault_DoesNotRequireAPassword()
    {
        Assert.False(DevelopmentSeedOptions.FromConfiguration(new ConfigurationBuilder().Build(), "Production").Enabled);
    }

    [Theory]
    [InlineData("Production", "SeedTestOnly!2026")]
    [InlineData("Staging", "SeedTestOnly!2026")]
    [InlineData("Development", null)]
    [InlineData("Development", "short")]
    [InlineData("Development", "NoSymbolPassword123")]
    [InlineData("Development", "SpaceOnlyPassword123 ")]
    public void EnabledOutsideDevelopment_OrWithoutAValidPassword_IsRejected(string environment, string? password)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DevelopmentSeed:Enabled"] = "true", ["DevelopmentSeed:Password"] = password }).Build();
        Assert.Throws<InvalidOperationException>(() => DevelopmentSeedOptions.FromConfiguration(config, environment));
    }
}

public sealed class DevelopmentSeedingTests : IClassFixture<TindyApiFactory>
{
    private const string Password = "SeedTestOnly!2026";
    private readonly TindyApiFactory _factory;
    public DevelopmentSeedingTests(TindyApiFactory factory) => _factory = factory;

    private static DevelopmentSeedOptions Options(string password = Password) => DevelopmentSeedOptions.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DevelopmentSeed:Enabled"] = "true", ["DevelopmentSeed:Password"] = password }).Build(), "Development");

    [Fact]
    public async Task Seed_IsAtomic_LoginWorks_AndFiftyProjectsPageThroughDiscovery_WithoutOverwritingEdits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var originalId = Guid.NewGuid();
        var originalProjectId = Guid.NewGuid();
        db.Users.Add(new User { UserId = originalId, Status = "active", CreatedAt = DateTime.UtcNow,
            Profile = new UserProfile { UserId = originalId, DisplayName = "Existing user — keep", CreatedAt = DateTime.UtcNow } });
        db.Projects.Add(new Project { ProjectId = originalProjectId, LeaderId = originalId, Name = "Existing project — keep",
            Category = "Web Application", Description = "Keep this row unchanged", Status = "draft", CreatedAt = DateTime.UtcNow,
            RecruitmentClosesAt = DateTime.UtcNow.AddDays(7) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Demonstrate rollback with an actual PostgreSQL failure late in the seed graph.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_development_seed_test() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Deliberate seed rollback test'; END $$;
            CREATE TRIGGER fail_development_seed_test BEFORE INSERT ON projects
            FOR EACH ROW EXECUTE FUNCTION fail_development_seed_test();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => TindySeedRunner.SeedAsync(db, development: Options()));
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Projects.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_development_seed_test ON projects; DROP FUNCTION fail_development_seed_test();");

        await TindySeedRunner.SeedAsync(db, development: Options());
        db.ChangeTracker.Clear();
        var users = await db.Users.Include(user => user.Profile).ThenInclude(profile => profile!.UserSkills)
            .Include(user => user.OnboardingSteps).Include(user => user.Credential).Include(user => user.UserRoles)
            .Include(user => user.ProfileExperiences).ThenInclude(experience => experience.ExperienceSkills)
            .Include(user => user.Certificates).AsSplitQuery().Where(user => user.Username != null && user.Username.StartsWith("demo"))
            .OrderBy(user => user.Username).ToListAsync();
        Assert.Equal(20, users.Count);
        Assert.All(users, user =>
        {
            Assert.True(user.IsOnboardingCompleted);
            Assert.Equal(7, user.OnboardingSteps.Count);
            Assert.Single(user.UserRoles);
            Assert.NotNull(user.Profile!.PreferredRoleId);
            Assert.InRange(user.Profile.UserSkills.Count, 5, 6);
            Assert.Equal(2, user.ProfileExperiences.Count);
            Assert.All(user.ProfileExperiences, experience => Assert.Equal(3, experience.ExperienceSkills.Count));
            Assert.InRange(user.Certificates.Count, 1, 2);
            Assert.True(new PasswordHasher().VerifyPassword(Password, user.Credential!.PasswordHash));
            Assert.Null(user.PrimaryEmailVerifiedAt);
            Assert.Null(user.Credential.EmailVerifiedAt);
        });
        Assert.Equal(40, await db.ProfileExperiences.CountAsync());
        Assert.Equal(30, await db.UserCertificates.CountAsync());
        Assert.Empty(await db.MediaAssets.ToListAsync());
        Assert.Equal(84, await db.Skills.CountAsync());
        Assert.Equal(10, await db.SkillCategories.CountAsync());
        Assert.All(await db.Skills.ToListAsync(), skill => Assert.True(db.SkillCategorySkills.Any(link => link.SkillId == skill.SkillId)));

        var projects = await db.Projects.Include(project => project.Positions).ThenInclude(position => position.PositionSkills)
            .Include(project => project.Members).AsSplitQuery().Where(project => project.ProjectId != originalProjectId).ToListAsync();
        Assert.Equal(50, projects.Count);
        Assert.Equal(6, projects.Select(project => project.LeaderId).Distinct().Count());
        Assert.All(projects, project =>
        {
            Assert.Equal("recruiting", project.Status);
            Assert.True(project.RecruitmentClosesAt > DateTime.UtcNow);
            var member = Assert.Single(project.Members);
            Assert.True(member.IsLeader);
            Assert.Equal(project.LeaderId, member.UserId);
            Assert.Equal(3, project.Positions.Count);
            Assert.Equal(project.MaxTeamCapacity - 1, project.Positions.Sum(position => position.RequiredCount));
            Assert.All(project.Positions, position => { Assert.Equal(0, position.FilledCount); Assert.Equal(4, position.PositionSkills.Count); });
        });

        using var client = _factory.CreateHttpsClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier = "demo20@tindy.test", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var auth = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = auth.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var profile = await Data(await client.GetAsync("/api/me/profile"));
        Assert.Equal(2, profile.RootElement.GetProperty("experiences_count").GetInt32());
        Assert.Equal(2, profile.RootElement.GetProperty("certificates_count").GetInt32());
        var seen = new HashSet<Guid>();
        var scores = new HashSet<int>();
        string? cursor = null;
        var pageSizes = new List<int>();
        do
        {
            using var page = await Data(await client.GetAsync("/api/discovery/projects?limit=20" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))));
            var data = page.RootElement;
            var items = data.GetProperty("items").EnumerateArray().ToArray();
            pageSizes.Add(items.Length);
            foreach (var card in items)
            {
                Assert.True(seen.Add(card.GetProperty("project_id").GetGuid()));
                Assert.Equal("rule-v1", card.GetProperty("algorithm_version").GetString());
                scores.Add(card.GetProperty("match_score").GetInt32());
            }
            cursor = data.GetProperty("has_more").GetBoolean() ? data.GetProperty("next_cursor").GetString() : null;
        } while (cursor is not null);
        Assert.Equal(new[] { 20, 20, 10 }, pageSizes);
        Assert.Equal(50, seen.Count);
        Assert.True(scores.Count > 1);

        var editedUser = users[^1];
        editedUser.Profile!.DisplayName = "Edited by tester";
        var changedHash = new PasswordHasher().HashPassword("ChangedByTester!2026");
        editedUser.Credential!.PasswordHash = changedHash;
        var editedProject = projects[0];
        editedProject.Status = "archived";
        editedProject.ArchivedAt = DateTime.UtcNow;
        editedProject.RecruitmentClosesAt = DateTime.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();
        // PostgreSQL stores microseconds, while DateTime can contain 100 ns ticks.
        var originalCutoff = await db.Projects.Where(project => project.ProjectId == editedProject.ProjectId)
            .Select(project => project.RecruitmentClosesAt).SingleAsync();
        db.ChangeTracker.Clear();
        TindySeedRunner.Seed(db, Options("AnotherSeedPassword!2026"));
        await TindySeedRunner.SeedAsync(db, development: Options());
        db.ChangeTracker.Clear();
        Assert.Equal(21, await db.Users.CountAsync());
        Assert.Equal(51, await db.Projects.CountAsync());
        Assert.Equal(150, await db.ProjectPositions.CountAsync());
        Assert.Equal(40, await db.ProfileExperiences.CountAsync());
        Assert.Equal(30, await db.UserCertificates.CountAsync());
        Assert.Equal("Edited by tester", await db.UserProfiles.Where(user => user.UserId == editedUser.UserId).Select(user => user.DisplayName).SingleAsync());
        Assert.Equal(changedHash, await db.UserCredentials.Where(user => user.UserId == editedUser.UserId).Select(user => user.PasswordHash).SingleAsync());
        Assert.Equal("archived", await db.Projects.Where(project => project.ProjectId == editedProject.ProjectId).Select(project => project.Status).SingleAsync());
        Assert.Equal(originalCutoff, await db.Projects.Where(project => project.ProjectId == editedProject.ProjectId).Select(project => project.RecruitmentClosesAt).SingleAsync());
        Assert.Equal("Existing user — keep", await db.UserProfiles.Where(user => user.UserId == originalId).Select(user => user.DisplayName).SingleAsync());
        Assert.Equal("Existing project — keep", await db.Projects.Where(project => project.ProjectId == originalProjectId).Select(project => project.Name).SingleAsync());
    }

    private static async Task<JsonDocument> Data(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return JsonDocument.Parse(envelope.RootElement.GetProperty("data").GetRawText());
        }
    }
}
