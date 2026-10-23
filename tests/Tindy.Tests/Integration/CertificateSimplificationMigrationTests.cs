using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class CertificateSimplificationMigrationTests
{
    private const string Previous = "20261023014312_Profile_CreateMediaAssetsAndAttachments";

    private static async Task<(Guid Owner, Guid Certificate, Guid[] Attachments)> SeedLegacy(TindyDbContext db, int count)
    {
        var owner = Guid.NewGuid();
        var certificate = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (user_id, status, is_onboarding_completed, created_at) VALUES ({owner}, {"active"}, {true}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_certificates (certificate_id, user_id, title, issuer, credential_id, issued_at, expires_at, created_at) VALUES ({certificate}, {owner}, {"AWS"}, {"Amazon"}, {"legacy-value"}, {new DateOnly(2025, 1, 1)}, {new DateOnly(2028, 1, 1)}, {now})");
        var attachments = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var media = Guid.NewGuid();
            var attachment = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO media_assets (media_asset_id, owner_user_id, purpose, storage_provider, storage_container, storage_key, status, scan_status, created_at) VALUES ({media}, {owner}, {"certificate"}, {"local"}, {"test"}, {media.ToString()}, {"rejected"}, {"threat_detected"}, {now})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_certificate_attachments (attachment_id, certificate_id, owner_user_id, media_asset_id, created_at) VALUES ({attachment}, {certificate}, {owner}, {media}, {now})");
            attachments.Add(attachment);
        }
        return (owner, certificate, attachments.ToArray());
    }

    [Fact]
    public async Task Migration_DuplicateGuardReportsRecords_AndLeavesSchemaAndDataIntact()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        var connection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { IncludeErrorDetail = true };
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(connection.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var (_, certificate, attachments) = await SeedLegacy(db, 3);
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Contains("resolve duplicate attachments manually", error.MessageText);
        Assert.Contains(certificate.ToString(), error.Detail!);
        Assert.All(attachments, id => Assert.Contains(id.ToString(), error.Detail!));
        Assert.Equal(3, await db.UserCertificateAttachments.CountAsync(a => a.CertificateId == certificate));
        Assert.Equal(3, await db.MediaAssets.CountAsync());
        Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_Profile_SimplifyCertificatesAndLimitAttachments"));
        Assert.Equal("legacy-value", await db.Database.SqlQueryRaw<string>("SELECT credential_id AS \"Value\" FROM user_certificates").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'user_certificates' AND column_name = 'description'").SingleAsync());
    }

    [Fact]
    public async Task Migration_UpDown_PreservesLinkedMedia_AndMatchesCurrentModel()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        await using var db = new TindyDbContext(new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var (_, certificate, attachments) = await SeedLegacy(db, 1);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var row = await db.UserCertificates.AsNoTracking().SingleAsync(c => c.CertificateId == certificate);
        Assert.Equal("AWS", row.Title);
        Assert.Null(row.Description);
        Assert.Equal(attachments[0], (await db.UserCertificateAttachments.SingleAsync()).AttachmentId);
        Assert.Single(await db.MediaAssets.ToListAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE user_certificates SET description = {new string('x', 2000)} WHERE certificate_id = {certificate}");
        var tooLong = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE user_certificates SET description = {new string('x', 2001)} WHERE certificate_id = {certificate}"));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, tooLong.SqlState);
        await migrator.MigrateAsync(Previous);
        Assert.Equal(attachments[0], (await db.UserCertificateAttachments.AsNoTracking().SingleAsync()).AttachmentId);
        Assert.Single(await db.MediaAssets.ToListAsync());
        // Down restores the old columns, not values destroyed by Up; recovery needs backup.
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM user_certificates WHERE credential_id IS NULL AND issued_at IS NULL AND expires_at IS NULL").SingleAsync());
    }
}
