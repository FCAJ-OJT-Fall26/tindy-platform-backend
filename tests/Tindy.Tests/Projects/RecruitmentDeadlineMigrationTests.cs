using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class RecruitmentDeadlineMigrationTests
{
    private const string Previous = "20261027034051_Project_CreateProjectAndPositionTables";

    [Fact]
    public async Task Migration_UpDown_PreservesExactLegacyCutoffsAndProfileHistory()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        await using var db = NewContext(postgres);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var owner = Guid.NewGuid();
        var now = RecruitmentDeadlineTests.Utc("2026-10-20T05:30:00Z");
        await SeedOwner(db, owner, now);
        var experience = Guid.NewGuid();
        var certificate = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_profiles (user_id, display_name, created_at) VALUES ({owner}, {"Original Profile"}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO profile_experiences (experience_id, user_id, project_name, role_title, description, created_at) VALUES ({experience}, {owner}, {"Historical Experience"}, {"Developer"}, {"Existing experience description"}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_certificates (certificate_id, user_id, title, issuer, created_at) VALUES ({certificate}, {owner}, {"Original Certificate"}, {"Issuer"}, {now})");

        var rows = new[]
        {
            (Id: Guid.NewGuid(), Created: now, Days: 14),
            (Id: Guid.NewGuid(), Created: RecruitmentDeadlineTests.Utc("2026-10-24T17:00:00Z"), Days: 7),
            (Id: Guid.NewGuid(), Created: RecruitmentDeadlineTests.Utc("2026-10-25T00:30:00Z"), Days: 14)
        };
        foreach (var row in rows) await SeedProject(db, row.Id, owner, row.Created, row.Days);
        // A DST-observing session must still preserve N * 24 UTC hours.
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/New_York'");
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());

        foreach (var row in rows)
        {
            var saved = await db.Projects.AsNoTracking().SingleAsync(p => p.ProjectId == row.Id);
            Assert.Equal(row.Created.AddDays(row.Days), saved.RecruitmentClosesAt);
            Assert.Equal(row.Days, await db.Projects.Where(p => p.ProjectId == row.Id)
                .Select(p => EF.Property<int>(p, "RecruitmentClosingDays")).SingleAsync());
        }
        Assert.Equal("Original Profile", (await db.UserProfiles.SingleAsync()).DisplayName);
        Assert.Equal(experience, (await db.ProfileExperiences.SingleAsync()).ExperienceId);
        Assert.Equal(certificate, (await db.UserCertificates.SingleAsync()).CertificateId);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'projects' AND column_name = 'recruitment_closes_at' AND is_nullable = 'NO' AND data_type = 'timestamp with time zone'").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE tablename = 'projects' AND indexname = 'ix_projects_recruitment_closes_at'").SingleAsync());

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'projects' AND column_name = 'recruitment_closes_at'").SingleAsync());
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM projects").SingleAsync());
        Assert.Single(await db.ProfileExperiences.ToListAsync());
        await migrator.MigrateAsync();
        Assert.Equal(rows[0].Created.AddDays(14), (await db.Projects.SingleAsync(p => p.ProjectId == rows[0].Id)).RecruitmentClosesAt);
    }

    [Fact]
    public async Task Migration_RefusesLossyRollback_WhenCalendarDeadlineWasEdited()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        await using var db = NewContext(postgres);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var created = RecruitmentDeadlineTests.Utc("2026-10-20T05:30:00Z");
        await SeedOwner(db, owner, created);
        await SeedProject(db, id, owner, created, 14);
        await migrator.MigrateAsync();
        var cutoff = RecruitmentDeadlineTests.Utc("2026-11-12T17:00:00Z");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE projects SET recruitment_closes_at = {cutoff} WHERE project_id = {id}");
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Contains("Cannot downgrade recruitment deadlines", error.MessageText);
        Assert.Equal(cutoff, (await db.Projects.SingleAsync()).RecruitmentClosesAt);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_Project_AddRecruitmentDeadline"));
    }

    [Fact]
    public async Task GeneralBrowsing_SortsByActualCutoff_AndKeepsExpiredProjectsReadable()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        await using var db = NewContext(postgres);
        await db.Database.MigrateAsync();
        var now = RecruitmentDeadlineTests.Utc("2026-10-30T05:00:00Z");
        var owner = Guid.NewGuid();
        await SeedOwner(db, owner, now);
        var repository = new ProjectRepository(db);
        var service = new ProjectService(repository, new ProjectTestClock(now));
        // No positions needed: general browsing is intentionally not discovery.
        var later = new Tindy.Application.Entities.Project
        {
            ProjectId = Guid.NewGuid(), LeaderId = owner, Name = "Later cutoff", Description = "General browse project description", Category = "Web",
            CreatedAt = now.AddDays(-30), RecruitmentClosesAt = now.AddDays(10)
        };
        var expired = new Tindy.Application.Entities.Project
        {
            ProjectId = Guid.NewGuid(), LeaderId = owner, Name = "Expired cutoff", Description = "General browse project description", Category = "Web",
            CreatedAt = now, RecruitmentClosesAt = now.AddHours(-1)
        };
        db.Projects.AddRange(later, expired);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await service.GetProjectsAsync(new GetProjectsQuery(SortBy: "deadline"));
        Assert.Equal(new[] { expired.ProjectId, later.ProjectId }, result.Items.Select(p => p.ProjectId));
        Assert.False(result.Items[0].IsRecruitmentOpen);
        Assert.True(result.Items[1].IsRecruitmentOpen);
        Assert.Equal("recruiting", (await service.GetProjectByIdAsync(expired.ProjectId)).Status);
    }

    private static TindyDbContext NewContext(TestPostgres postgres) => new(
        new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private static Task SeedOwner(TindyDbContext db, Guid owner, DateTime created) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (user_id, status, is_onboarding_completed, created_at) VALUES ({owner}, {"active"}, {true}, {created})");

    private static Task SeedProject(TindyDbContext db, Guid id, Guid owner, DateTime created, int days) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO projects (project_id, leader_id, name, description, category, recruitment_closing_days, goals, created_at) VALUES ({id}, {owner}, {"Legacy Project"}, {"Legacy project description"}, {"Web"}, {days}, '[]'::jsonb, {created})");
}
