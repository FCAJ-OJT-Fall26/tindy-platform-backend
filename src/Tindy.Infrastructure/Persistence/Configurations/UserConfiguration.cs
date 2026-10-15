using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");
        
        builder.Property(x => x.Username)
            .HasColumnName("username")
            .HasMaxLength(50)
            .IsRequired(false);        

        builder.Property(x => x.UsernameNormalized)
            .HasColumnName("username_normalized")
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(x => x.PrimaryEmail)
            .HasColumnName("primary_email")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.PrimaryEmailNormalized)
            .HasColumnName("primary_email_normalized")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.PrimaryEmailVerifiedAt)
            .HasColumnName("primary_email_verified_at")
            .IsRequired(false);

        // PostgreSQL permits multiple NULL values in a unique index.
        builder.HasIndex(x => x.PrimaryEmailNormalized)
            .IsUnique()
            .HasDatabaseName("ux_users_primary_email_normalized");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.IsOnboardingCompleted)
            .HasColumnName("is_onboarding_completed")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(x => x.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(x => x.Username)
            .HasDatabaseName("ix_users_username");

        builder.HasIndex(x => x.UsernameNormalized)
            .IsUnique()
            .HasDatabaseName("ux_users_username_normalized");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_users_status");           

    }
}