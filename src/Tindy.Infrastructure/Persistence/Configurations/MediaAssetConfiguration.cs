using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets", table =>
        {
            table.HasCheckConstraint("ck_media_assets_purpose", "purpose IN ('avatar', 'cover', 'certificate')");
            table.HasCheckConstraint("ck_media_assets_storage_provider", "storage_provider IN ('local', 's3')");
            table.HasCheckConstraint("ck_media_assets_storage_identity", "btrim(storage_container) <> '' AND btrim(storage_key) <> ''");
            table.HasCheckConstraint("ck_media_assets_status", "status IN ('pending_upload', 'processing', 'active', 'rejected', 'pending_delete')");
            table.HasCheckConstraint("ck_media_assets_scan_status", "scan_status IN ('pending', 'clean', 'threat_detected', 'unsupported', 'access_denied', 'failed', 'not_required')");
            table.HasCheckConstraint("ck_media_assets_scan_provider", "scan_provider IS NULL OR scan_provider IN ('clamav', 'guardduty')");
            table.HasCheckConstraint("ck_media_assets_positive_sizes", "(file_size_bytes IS NULL OR file_size_bytes > 0) AND reserved_bytes >= 0");
            table.HasCheckConstraint("ck_media_assets_positive_dimensions", "(width IS NULL OR width > 0) AND (height IS NULL OR height > 0)");
            table.HasCheckConstraint("ck_media_assets_cleanup_attempts", "cleanup_attempts >= 0");
            // Keep all uploaded PDFs inaccessible until a trusted scan result is persisted.
            table.HasCheckConstraint("ck_media_assets_active_safe", "status <> 'active' OR (file_size_bytes IS NOT NULL AND file_size_bytes > 0 AND content_type IS NOT NULL AND reserved_bytes = 0 AND " +
                "((content_type = 'application/pdf' AND scan_status = 'clean') OR (content_type <> 'application/pdf' AND scan_status IN ('not_required', 'clean'))))");
        });

        builder.HasKey(x => x.MediaAssetId);
        builder.Property(x => x.MediaAssetId).HasColumnName("media_asset_id").ValueGeneratedNever();
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(30).IsRequired();
        builder.Property(x => x.StorageProvider).HasColumnName("storage_provider").HasMaxLength(20).IsRequired();
        builder.Property(x => x.StorageContainer).HasColumnName("storage_container").HasMaxLength(255).IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ObjectVersionId).HasColumnName("object_version_id").HasMaxLength(255);
        builder.Property(x => x.ObjectEtag).HasColumnName("object_etag").HasMaxLength(255);
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255);
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
        builder.Property(x => x.ReservedBytes).HasColumnName("reserved_bytes").HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.Width).HasColumnName("width");
        builder.Property(x => x.Height).HasColumnName("height");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("pending_upload").IsRequired();
        builder.Property(x => x.ScanStatus).HasColumnName("scan_status").HasMaxLength(30).HasDefaultValue("not_required").IsRequired();
        builder.Property(x => x.ScanProvider).HasColumnName("scan_provider").HasMaxLength(30);
        builder.Property(x => x.ScanCompletedAt).HasColumnName("scan_completed_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.UploadExpiresAt).HasColumnName("upload_expires_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ActivatedAt).HasColumnName("activated_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeleteRequestedAt).HasColumnName("delete_requested_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.CleanupAttempts).HasColumnName("cleanup_attempts").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.NextCleanupAt).HasColumnName("next_cleanup_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastCleanupError).HasColumnName("last_cleanup_error").HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        // Composite principal key prevents cross-user profile/attachment references.
        builder.HasAlternateKey(x => new { x.MediaAssetId, x.OwnerUserId })
            .HasName("ak_media_assets_media_asset_id_owner_user_id");
        builder.HasOne(x => x.Owner).WithMany(x => x.MediaAssets)
            .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StorageProvider, x.StorageContainer, x.StorageKey })
            .IsUnique().HasDatabaseName("ux_media_assets_storage_object");
        builder.HasIndex(x => new { x.OwnerUserId, x.Purpose })
            .HasDatabaseName("ix_media_assets_owner_purpose");
        builder.HasIndex(x => new { x.Status, x.NextCleanupAt })
            .HasFilter("status = 'pending_delete'")
            .HasDatabaseName("ix_media_assets_cleanup");
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasFilter("status = 'processing'")
            .HasDatabaseName("ix_media_assets_processing");
        builder.HasIndex(x => x.UploadExpiresAt)
            .HasFilter("status = 'pending_upload'")
            .HasDatabaseName("ix_media_assets_expiring_uploads");
    }
}
