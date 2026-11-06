using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tindy.Infrastructure.Persistence;
using Tindy.Application.Entities;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ProjectGroupChatMigrationTests
{
    [Fact]
    public async Task BackfillsExistingProjectsAndMemberships_AndKeepsDirectHistory()
    {
        await using var postgres = new TestPostgres(); await postgres.StartAsync();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var feature = migrations.Single(x => x.EndsWith("_Chat_AddProjectGroups"));
        var before = migrations[Array.IndexOf(migrations, feature) - 1];
        await migrator.MigrateAsync(before);
        var leader = RecruitmentData.User(); var former = RecruitmentData.User(); var now = DateTime.UtcNow;
        db.Users.AddRange(leader, former);
        var project = new Project { ProjectId = Guid.NewGuid(), LeaderId = leader.UserId, Name = "Archived project", Description = "Migration fixture", Category = "Test", Status = "archived", ArchivedAt = now, RecruitmentClosesAt = now, CreatedAt = now };
        project.Members.Add(new ProjectMember { UserId = leader.UserId, RoleTitle = "Leader", IsLeader = true, JoinedAt = now });
        project.Members.Add(new ProjectMember { UserId = former.UserId, RoleTitle = "Developer", Status = "left", JoinedAt = now });
        db.Projects.Add(project); await db.SaveChangesAsync();
        var direct = Guid.NewGuid(); var message = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO conversations(conversation_id,type,status,consent_source,created_at,updated_at) VALUES ({direct},{"direct"},{"active"},{"legacy"},{now},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chat_messages(message_id,conversation_id,sender_id,body,created_at) VALUES ({message},{direct},{leader.UserId},{"Direct history"},{now})");
        await migrator.MigrateAsync(feature);
        var room = await db.Conversations.Include(c => c.Participants).SingleAsync(c => c.ProjectId == project.ProjectId);
        Assert.Equal("project", room.Type); Assert.Equal(2, room.Participants.Count);
        Assert.Null(room.Participants.Single(p => p.UserId == leader.UserId).LeftAt);
        Assert.NotNull(room.Participants.Single(p => p.UserId == former.UserId).LeftAt);
        Assert.Equal("text", (await db.ChatMessages.SingleAsync(m => m.MessageId == message)).MessageType);
        await migrator.MigrateAsync(before);
        Assert.Equal("Direct history", await db.Database.SqlQuery<string>($"SELECT body AS \"Value\" FROM chat_messages WHERE message_id={message}").SingleAsync());
    }
}
