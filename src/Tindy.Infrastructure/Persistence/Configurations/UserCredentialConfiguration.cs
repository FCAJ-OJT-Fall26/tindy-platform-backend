using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class UserCredentialConfiguration
    : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("user_credentials");

        builder.HasKey(x => x.UserCredentialId);

        builder.Property(x => x.UserCredentialId)
            .HasColumnName("user_credential_id");

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.EmailNormalized)
            .HasColumnName("email_normalized")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.EmailVerifiedAt)
            .HasColumnName("email_verified_at");

        builder.Property(x => x.PasswordChangedAt)
            .HasColumnName("password_changed_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("ux_user_credentials_user_id");

        builder.HasIndex(x => x.Email)
            .HasDatabaseName("ix_user_credentials_email");

        builder.HasIndex(x => x.EmailNormalized)
            .IsUnique()
            .HasDatabaseName("ux_user_credentials_email_normalized");

        builder.HasOne(x => x.User)
            .WithOne(x => x.Credential)
            .HasForeignKey<UserCredential>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}