using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class UserCertificateConfiguration : IEntityTypeConfiguration<UserCertificate>
{
    public void Configure(EntityTypeBuilder<UserCertificate> builder)
    {
        builder.ToTable("user_certificates", table =>
        {
            table.HasCheckConstraint("ck_user_certificates_dates_order", "issued_at IS NULL OR expires_at IS NULL OR expires_at >= issued_at");
        });

        builder.HasKey(x => x.CertificateId);
        builder.Property(x => x.CertificateId).HasColumnName("certificate_id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Issuer).HasColumnName("issuer").HasMaxLength(255).IsRequired();
        builder.Property(x => x.CredentialId).HasColumnName("credential_id").HasMaxLength(255);
        builder.Property(x => x.CredentialUrl).HasColumnName("credential_url").HasMaxLength(1000);
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at").HasColumnType("date");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("date");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.User).WithMany(x => x.Certificates)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_user_certificates_user_id");
    }
}
