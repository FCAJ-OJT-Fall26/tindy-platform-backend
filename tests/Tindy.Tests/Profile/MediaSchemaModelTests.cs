using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Xunit;

namespace Tindy.Tests.Profile;

/// <summary>EF metadata tests; no database/Docker required.</summary>
public sealed class MediaSchemaModelTests
{
    private static TindyDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=none;Password=none").Options;
        return new TindyDbContext(options);
    }

    [Fact]
    public void MediaAssets_HavePurposeScanLifecycleSafetyChecks_AndStorageUniqueness()
    {
        using var db = NewContext();
        var media = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MediaAsset))!;
        Assert.Equal("media_assets", media.GetTableName());
        var checks = media.GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);
        foreach (var name in new[] { "ck_media_assets_purpose", "ck_media_assets_storage_provider",
            "ck_media_assets_status", "ck_media_assets_scan_status", "ck_media_assets_active_safe",
            "ck_media_assets_positive_sizes", "ck_media_assets_positive_dimensions" })
            Assert.True(checks.ContainsKey(name), $"Missing {name}");
        Assert.Contains("application/pdf", checks["ck_media_assets_active_safe"]);
        Assert.Contains("scan_status = 'clean'", checks["ck_media_assets_active_safe"]);
        Assert.Contains(media.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(x => x.Name).SequenceEqual(new[] { "StorageProvider", "StorageContainer", "StorageKey" }));
        Assert.Contains(media.GetKeys(), k => k.Properties.Select(x => x.Name)
            .SequenceEqual(new[] { "MediaAssetId", "OwnerUserId" }));
        Assert.Equal(DeleteBehavior.Restrict,
            media.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(User)).DeleteBehavior);
    }

    [Fact]
    public void Profile_AvatarCoverAndOAuth_RequireSameOwnerAndNoCascades()
    {
        using var db = NewContext();
        var profile = db.Model.FindEntityType(typeof(UserProfile))!;
        foreach (var fkProperties in new[] {
            new[] { "AvatarAssetId", "UserId" }, new[] { "CoverAssetId", "UserId" } })
        {
            var fk = Assert.Single(profile.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(MediaAsset)
                && f.Properties.Select(p => p.Name).SequenceEqual(fkProperties));
            Assert.Equal(new[] { "MediaAssetId", "OwnerUserId" },
                fk.PrincipalKey.Properties.Select(p => p.Name));
            Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        }
        var oauth = Assert.Single(profile.GetForeignKeys(), f =>
            f.PrincipalEntityType.ClrType == typeof(ExternalAuthAccount));
        Assert.Equal(new[] { "UserId", "AvatarOAuthProvider" }, oauth.Properties.Select(x => x.Name));
        Assert.Equal(DeleteBehavior.NoAction, oauth.DeleteBehavior);
        Assert.True(profile.FindProperty(nameof(UserProfile.AvatarSource))!.IsNullable);
        Assert.NotNull(profile.FindProperty(nameof(UserProfile.AvatarUrl)));
        Assert.NotNull(profile.FindProperty(nameof(UserProfile.CoverImageUrl)));
    }

    [Fact]
    public void Attachments_HaveTwoOwnerForeignKeys_RestrictDeletes_AndOwnerOnlyDefault()
    {
        using var db = NewContext();
        var attachment = db.Model.FindEntityType(typeof(UserCertificateAttachment))!;
        Assert.Equal("user_certificate_attachments", attachment.GetTableName());
        var certificate = Assert.Single(attachment.GetForeignKeys(), f =>
            f.PrincipalEntityType.ClrType == typeof(UserCertificate));
        Assert.Equal(new[] { "CertificateId", "OwnerUserId" }, certificate.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.NoAction, certificate.DeleteBehavior);
        var media = Assert.Single(attachment.GetForeignKeys(), f =>
            f.PrincipalEntityType.ClrType == typeof(MediaAsset));
        Assert.Equal(new[] { "MediaAssetId", "OwnerUserId" }, media.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.NoAction, media.DeleteBehavior);
        Assert.Equal("owner_only", attachment.FindProperty(nameof(UserCertificateAttachment.Visibility))!.GetDefaultValue());
        Assert.Contains(attachment.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "MediaAssetId" }));
    }
}
