using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

/// <summary>
/// Requires Docker. Runs on an isolated PostgreSQL container; never modifies a developer database.
/// Migration Up/Down and actual PostgreSQL ownership-FK checks (not just EF metadata).
/// </summary>
public sealed class MediaSchemaMigrationTests
{
    private const string PreviousMigration = "20261020130314_Auth_AddOAuthLinkAvatarMetadata";

    [Fact]
    public async Task Migration_UpDown_PreservesExistingProfileCertificateAndRejectsForeignOwners()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new TindyDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var certificate = Guid.NewGuid();
        var now = DateTime.UtcNow;
        foreach (var id in new[] { ownerA, ownerB })
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO users (user_id, status, is_onboarding_completed, created_at) VALUES ({id}, {"active"}, {true}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO user_profiles (user_id, display_name, avatar_url, cover_image_url, created_at) VALUES ({ownerA}, {"Owner A"}, {"https://legacy.example/avatar"}, {"https://legacy.example/cover"}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO user_certificates (certificate_id, user_id, title, issuer, created_at) VALUES ({certificate}, {ownerA}, {"AWS"}, {"AWS"}, {now})");

        await db.Database.MigrateAsync();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), id =>
            id.EndsWith("_Profile_CreateMediaAssetsAndAttachments", StringComparison.Ordinal));
        Assert.Equal("https://legacy.example/avatar", (await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == ownerA)).AvatarUrl);
        Assert.Null((await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == ownerA)).AvatarSource);
        Assert.Equal("AWS", (await db.UserCertificates.AsNoTracking().SingleAsync(x => x.CertificateId == certificate)).Title);

        var foreignAvatar = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO media_assets (media_asset_id, owner_user_id, purpose, storage_provider, storage_container, storage_key, created_at) VALUES ({foreignAvatar}, {ownerB}, {"avatar"}, {"local"}, {"test"}, {"avatar/b"}, {now})");
        var avatarEx = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE user_profiles SET avatar_source = {"uploaded"}, avatar_asset_id = {foreignAvatar} WHERE user_id = {ownerA}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, avatarEx.SqlState);

        var foreignCertificate = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO media_assets (media_asset_id, owner_user_id, purpose, storage_provider, storage_container, storage_key, created_at) VALUES ({foreignCertificate}, {ownerB}, {"certificate"}, {"local"}, {"test"}, {"certificate/b"}, {now})");
        var attachmentEx = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO user_certificate_attachments (attachment_id, certificate_id, owner_user_id, media_asset_id, created_at) VALUES ({Guid.NewGuid()}, {certificate}, {ownerA}, {foreignCertificate}, {now})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, attachmentEx.SqlState);

        // A valid owner-owned media reference must work as well.
        var ownAvatar = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO media_assets (media_asset_id, owner_user_id, purpose, storage_provider, storage_container, storage_key, created_at) VALUES ({ownAvatar}, {ownerA}, {"avatar"}, {"local"}, {"test"}, {"avatar/a"}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE user_profiles SET avatar_source = {"uploaded"}, avatar_asset_id = {ownAvatar} WHERE user_id = {ownerA}");

        // Rolling back deliberately drops the new media schema only; legacy data must remain.
        await migrator.MigrateAsync(PreviousMigration);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT avatar_url FROM user_profiles WHERE user_id = @user_id", connection);
        command.Parameters.AddWithValue("user_id", ownerA);
        Assert.Equal("https://legacy.example/avatar", (string?)await command.ExecuteScalarAsync());
        await using var checkCertificate = new NpgsqlCommand(
            "SELECT title FROM user_certificates WHERE certificate_id = @certificate_id", connection);
        checkCertificate.Parameters.AddWithValue("certificate_id", certificate);
        Assert.Equal("AWS", (string?)await checkCertificate.ExecuteScalarAsync());
    }
}
