using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class NotificationSchemaTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Fact]
    public async Task Database_EnforcesTypeUniqueEventForeignKeysAndRestrictiveHistory()
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var invitation = TeammateTestData.Invitation(data); db.ProjectInvitations.Add(invitation); await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        Task Insert(Guid recipient, Guid invitationId, string type, Guid? actor = null) => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notifications (notification_id,recipient_user_id,actor_user_id,invitation_id,type,payload,created_at) VALUES ({Guid.NewGuid()},{recipient},{actor},{invitationId},{type},'{{}}'::jsonb,{now})");
        await Insert(data.Applicant, invitation.InvitationId, "invitation_received");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Insert(data.Applicant, invitation.InvitationId, "invitation_received"));
        Assert.Equal("ux_notifications_invitation_event_recipient", duplicate.ConstraintName);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => Insert(data.Applicant, invitation.InvitationId, "invitation_expired"));
        Assert.Equal("ck_notifications_type", invalid.ConstraintName);
        foreach (var type in new[] { "invitation_accepted", "invitation_declined", "invitation_cancelled" }) await Insert(data.Applicant, invitation.InvitationId, type);
        var invitationFk = await Assert.ThrowsAsync<PostgresException>(() => Insert(data.Applicant, Guid.NewGuid(), "invitation_received"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, invitationFk.SqlState);
        var recipientFk = await Assert.ThrowsAsync<PostgresException>(() => Insert(Guid.NewGuid(), invitation.InvitationId, "invitation_received"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, recipientFk.SqlState);
        var actorFk = await Assert.ThrowsAsync<PostgresException>(() => Insert(data.Outsider, invitation.InvitationId, "invitation_received", Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, actorFk.SqlState);
        var deleteInvitation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM project_invitations WHERE invitation_id={invitation.InvitationId}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, deleteInvitation.SqlState);
        // Outsider is referenced solely by these notifications, not invitation history.
        await Insert(data.Outsider, invitation.InvitationId, "invitation_received");
        var deleteRecipient = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE user_id={data.Outsider}"));
        Assert.Equal("FK_notifications_users_recipient_user_id", deleteRecipient.ConstraintName);
        var actor = RecruitmentData.User(); db.Users.Add(actor); await db.SaveChangesAsync();
        await Insert(data.Outsider, invitation.InvitationId, "invitation_declined", actor.UserId);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE user_id={actor.UserId}");
        Assert.Null(await db.Notifications.Where(n => n.RecipientUserId == data.Outsider && n.Type == "invitation_declined").Select(n => n.ActorUserId).SingleAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Model_MapsJsonbRequiredColumnsAndPaginationIndexes()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var entity = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(Notification))!;
        Assert.Equal("notifications", entity.GetTableName());
        Assert.Equal("jsonb", entity.FindProperty(nameof(Notification.Payload))!.GetColumnType());
        Assert.False(entity.FindProperty(nameof(Notification.InvitationId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(Notification.RecipientUserId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Notification.ActorUserId))!.IsNullable);
        Assert.Equal(60, entity.FindProperty(nameof(Notification.Type))!.GetMaxLength());
        var history = Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == "ix_notifications_recipient_history");
        Assert.Equal(new[] { nameof(Notification.RecipientUserId), nameof(Notification.CreatedAt), nameof(Notification.NotificationId) }, history.Properties.Select(p => p.Name));
        Assert.Equal(new[] { false, true, true }, history.IsDescending);
        var unread = Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == "ix_notifications_recipient_unread");
        Assert.Equal("read_at IS NULL", unread.GetFilter());
        Assert.True(Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == "ux_notifications_invitation_event_recipient").IsUnique);
    }

    [Fact]
    public async Task Migration_UpDownUp_PreservesInvitations_DoesNotBackfill_AndMatchesSnapshot()
    {
        await using var postgres = new TestPostgres(); await postgres.StartAsync();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        const string before = "20261103042556_Discovery_CreateTeammateRecruitmentTables";
        await migrator.MigrateAsync(before); await TindySeedRunner.SeedAsync(db);
        var leader = RecruitmentData.User(); var member = RecruitmentData.User(); db.Users.AddRange(leader, member);
        var role = await db.ProfessionalRoles.Select(r => r.ProfessionalRoleId).FirstAsync();
        var project = RecruitmentData.Project(leader.UserId, role, "Web"); db.Projects.Add(project);
        var now = DateTime.UtcNow;
        var invitation = new ProjectInvitation { InvitationId = Guid.NewGuid(), ProjectId = project.ProjectId,
            PositionId = project.Positions.First().PositionId, InvitedUserId = member.UserId, InvitingLeaderId = leader.UserId,
            CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddHours(168) };
        db.ProjectInvitations.Add(invitation); await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        db.Notifications.Add(NotificationHttpTests.Row(invitation.InvitationId, member.UserId, now)); await db.SaveChangesAsync();
        await migrator.MigrateAsync(before);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema='public' AND table_name='notifications'").SingleAsync());
        Assert.Equal(invitation.InvitationId, await db.ProjectInvitations.Select(i => i.InvitationId).SingleAsync());
        await migrator.MigrateAsync();
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
