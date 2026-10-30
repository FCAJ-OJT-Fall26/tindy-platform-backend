using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class RecruitmentSchemaTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public RecruitmentSchemaTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CompositeForeignKeys_RejectCrossProjectPositionReferences_AndStatusIsConstrained()
    {
        var first = await ApplicationTestData.Seed(_factory);
        var second = await ApplicationTestData.Seed(_factory);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var now = DateTime.UtcNow;
        var invalidApplication = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_applications (application_id, project_id, position_id, applicant_id, status, created_at, updated_at) VALUES ({Guid.NewGuid()}, {first.Project}, {second.Position}, {first.Applicant}, 'pending', {now}, {now})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, invalidApplication.SqlState);
        var invalidMembership = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_members (project_id, user_id, position_id, role_title, joined_at) VALUES ({first.Project}, {first.Applicant}, {second.Position}, 'Developer', {now})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, invalidMembership.SqlState);
        var invalidStatus = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_applications (application_id, project_id, position_id, applicant_id, status, created_at, updated_at) VALUES ({Guid.NewGuid()}, {first.Project}, {first.Position}, {first.Applicant}, 'interested', {now}, {now})"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidStatus.SqlState);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task InteractionAndApplicationMigrations_UpDownUp_PreservePreexistingProjectAndLeader()
    {
        await using var postgres = new TestPostgres(); await postgres.StartAsync();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        const string before = "20261030031756_Project_AddRecruitmentDeadline";
        await migrator.MigrateAsync(before);
        var user = Guid.NewGuid(); var project = Guid.NewGuid(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (user_id,status,is_onboarding_completed,created_at) VALUES ({user},'active',TRUE,{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO projects (project_id,leader_id,name,description,category,goals,created_at,recruitment_closes_at) VALUES ({project},{user},'Existing Project','Historical project description','Web','[]'::jsonb,{now},{now.AddDays(14)})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_members (project_id,user_id,role_title,is_leader,joined_at) VALUES ({project},{user},'Project Lead',TRUE,{now})");
        await migrator.MigrateAsync();
        Assert.Null((await db.ProjectMembers.AsNoTracking().SingleAsync()).PositionId);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await migrator.MigrateAsync(before);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM project_members").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name='project_members' AND column_name='position_id'").SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(project, (await db.Projects.SingleAsync()).ProjectId);
        Assert.Null((await db.ProjectMembers.SingleAsync()).PositionId);
    }
}
