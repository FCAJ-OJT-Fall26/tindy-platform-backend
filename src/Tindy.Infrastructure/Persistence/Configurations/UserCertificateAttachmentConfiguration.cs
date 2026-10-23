using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class UserCertificateAttachmentConfiguration : IEntityTypeConfiguration<UserCertificateAttachment>
{
    public void Configure(EntityTypeBuilder<UserCertificateAttachment> builder)
    {
        builder.ToTable("user_certificate_attachments", table =>
        {
            table.HasCheckConstraint("ck_user_certificate_attachments_visibility", "visibility IN ('owner_only', 'members')");
        });

        builder.HasKey(x => x.AttachmentId);
        builder.Property(x => x.AttachmentId).HasColumnName("attachment_id").ValueGeneratedNever();
        builder.Property(x => x.CertificateId).HasColumnName("certificate_id").IsRequired();
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(x => x.MediaAssetId).HasColumnName("media_asset_id").IsRequired();
        builder.Property(x => x.Visibility).HasColumnName("visibility").HasMaxLength(20)
            .HasDefaultValue("owner_only").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Certificate).WithMany(x => x.Attachments)
            .HasForeignKey(x => new { x.CertificateId, x.OwnerUserId })
            .HasPrincipalKey(x => new { x.CertificateId, x.UserId })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.MediaAsset).WithMany()
            .HasForeignKey(x => new { x.MediaAssetId, x.OwnerUserId })
            .HasPrincipalKey(x => new { x.MediaAssetId, x.OwnerUserId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.MediaAssetId).IsUnique()
            .HasDatabaseName("ux_user_certificate_attachments_media_asset_id");
        builder.HasIndex(x => x.CertificateId).IsUnique()
            .HasDatabaseName("ux_user_certificate_attachments_certificate_id");
        builder.HasIndex(x => new { x.CertificateId, x.OwnerUserId })
            .HasDatabaseName("ix_user_certificate_attachments_certificate_owner");
        builder.HasIndex(x => x.OwnerUserId).HasDatabaseName("ix_user_certificate_attachments_owner_user_id");
    }
}
