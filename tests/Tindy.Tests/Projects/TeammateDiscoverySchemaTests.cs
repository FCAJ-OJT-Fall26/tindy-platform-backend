using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class TeammateDiscoverySchemaTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Fact]
    public async Task ExpiryConstraint_RejectsCalendarDaysAcrossDst_ButAcceptsElapsedHours()
    {
        var data = await ApplicationTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/New_York'");
        var created = new DateTime(2026, 3, 7, 17, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_invitations (invitation_id,project_id,position_id,invited_user_id,inviting_leader_id,status,created_at,updated_at,expires_at) VALUES ({Guid.NewGuid()},{data.Project},{data.Position},{data.Applicant},{data.Leader},'pending',{created},{created},{created.AddHours(168)})");
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_invitations (invitation_id,project_id,position_id,invited_user_id,inviting_leader_id,status,created_at,updated_at,expires_at) VALUES ({Guid.NewGuid()},{data.Project},{data.Position},{data.Outsider},{data.Leader},'pending',{created},{created},{created} + interval '7 days')"));
        Assert.Equal("ck_project_invitations_expiry", invalid.ConstraintName);
    }
    [Fact]
    public async Task PendingIndex_CompositeForeignKey_ChecksAndHistory_AreEnforcedInPostgres()
    {
        var first = await ApplicationTestData.Seed(factory);
        var second = await ApplicationTestData.Seed(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var now = DateTime.UtcNow;
        async Task Insert(Guid id, Guid position, Guid recipient, string status, DateTime expiry, DateTime? response = null) =>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO project_invitations (invitation_id,project_id,position_id,invited_user_id,inviting_leader_id,status,created_at,updated_at,expires_at,responded_at) VALUES ({id},{first.Project},{position},{recipient},{first.Leader},{status},{now},{response ?? now},{expiry},{response})");
        var id = Guid.NewGuid();
        await Insert(id, first.Position, first.Applicant, "pending", now.AddHours(168));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Insert(Guid.NewGuid(), first.OtherPosition, first.Applicant, "pending", now.AddHours(168)));
        Assert.Equal("ux_project_invitations_pending", duplicate.ConstraintName);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => Insert(Guid.NewGuid(), second.Position, first.Outsider, "pending", now.AddHours(168)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        foreach (var status in new[] { "invalid", "accepted", "declined" })
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => Insert(Guid.NewGuid(), first.Position, first.Outsider, status, now.AddHours(168)))).SqlState);
        Assert.Equal("ck_project_invitations_expiry", (await Assert.ThrowsAsync<PostgresException>(() => Insert(Guid.NewGuid(), first.Position, first.Outsider, "pending", now.AddHours(167)))).ConstraintName);
        Assert.Equal("ck_project_invitations_distinct_users", (await Assert.ThrowsAsync<PostgresException>(() => Insert(Guid.NewGuid(), first.Position, first.Leader, "pending", now.AddHours(168)))).ConstraintName);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE project_invitations SET status='cancelled' WHERE invitation_id={id}");
        await Insert(Guid.NewGuid(), first.OtherPosition, first.Applicant, "pending", now.AddHours(168));
        Assert.Equal(2, await db.ProjectInvitations.CountAsync(i => i.ProjectId == first.Project));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM project_positions WHERE position_id={first.Position}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, delete.SqlState);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migrations_UpDownUp_DefaultFalse_AndElapsedHoursAcrossDst()
    {
        await using var postgres = new TestPostgres(); await postgres.StartAsync();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        const string before = "20261030045102_Chat_CreateConversationAndMessageTables";
        await migrator.MigrateAsync(before);
        var user = Guid.NewGuid(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (user_id,status,created_at) VALUES ({user},'active',{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_profiles (user_id,display_name,created_at) VALUES ({user},'Existing member',{now})");
        await migrator.MigrateAsync();
        Assert.False((await db.UserProfiles.AsNoTracking().SingleAsync()).OpenToTeamInvitations);
        // Hours, unlike calendar days, retain elapsed duration across the DST transition.
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/New_York'");
        Assert.Equal(604800m, await db.Database.SqlQueryRaw<decimal>("SELECT extract(epoch FROM (timestamptz '2026-03-07 12:00:00-05' + interval '168 hours') - timestamptz '2026-03-07 12:00:00-05') AS \"Value\"").SingleAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await migrator.MigrateAsync(before);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM user_profiles").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name='user_profiles' AND column_name='open_to_team_invitations'").SingleAsync());
        await migrator.MigrateAsync();
        Assert.False((await db.UserProfiles.AsNoTracking().SingleAsync()).OpenToTeamInvitations);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task InteractionConstraints_AndPositionCascade_AreRealDatabaseRules()
    {
        var data = await ApplicationTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO teammate_discovery_interactions (project_id,position_id,candidate_user_id,action,created_at,updated_at) VALUES ({data.Project},{data.Position},{data.Applicant},'saved',{now},{now})");
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE teammate_discovery_interactions SET action='interested' WHERE project_id={data.Project}"));
        Assert.Equal("ck_teammate_discovery_interactions_action", invalid.ConstraintName);
        var invalidTime = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE teammate_discovery_interactions SET updated_at={now.AddSeconds(-1)} WHERE project_id={data.Project}"));
        Assert.Equal("ck_teammate_discovery_interactions_timestamps", invalidTime.ConstraintName);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM project_positions WHERE position_id={data.Position}");
        Assert.False(await db.TeammateDiscoveryInteractions.AnyAsync(x => x.ProjectId == data.Project));
    }
}
