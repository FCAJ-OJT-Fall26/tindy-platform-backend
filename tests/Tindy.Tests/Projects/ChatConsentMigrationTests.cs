using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;
namespace Tindy.Tests.Projects;

public sealed class ChatConsentMigrationTests
{
    [Fact]
    public async Task ExistingHistoryGetsLegacyConsent_EmptyRoomDoesNot_AndRollbackPreservesHistory()
    {
        await using var postgres = new TestPostgres(); await postgres.StartAsync();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var feature = migrations.Single(x => x.EndsWith("_Chat_AddRequestsConsentAndBlocks"));
        var before = migrations[Array.IndexOf(migrations, feature) - 1];
        await migrator.MigrateAsync(before);
        var user = Guid.NewGuid(); var withHistory = Guid.NewGuid(); var empty = Guid.NewGuid(); var message = Guid.NewGuid(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users(user_id,username,status,is_onboarding_completed,created_at) VALUES ({user},{"migration-test"},{"active"},{true},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO conversations(conversation_id,type,status,created_at,updated_at) VALUES ({withHistory},{"direct"},{"active"},{now},{now}),({empty},{"direct"},{"active"},{now},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chat_messages(message_id,conversation_id,sender_id,body,created_at) VALUES ({message},{withHistory},{user},{"Retained history"},{now})");
        await migrator.MigrateAsync(feature);
        Assert.Equal("legacy", await db.Conversations.Where(x => x.ConversationId == withHistory).Select(x => x.ConsentSource).SingleAsync());
        Assert.Null(await db.Conversations.Where(x => x.ConversationId == empty).Select(x => x.ConsentSource).SingleAsync());
        await migrator.MigrateAsync(before);
        Assert.Equal("Retained history", await db.Database.SqlQuery<string>($"SELECT body AS \"Value\" FROM chat_messages WHERE message_id={message}").SingleAsync());
    }
}
