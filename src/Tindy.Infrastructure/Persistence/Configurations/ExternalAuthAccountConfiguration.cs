using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class ExternalAuthAccountConfiguration
    : IEntityTypeConfiguration<ExternalAuthAccount>
{
    public void Configure(
        EntityTypeBuilder<ExternalAuthAccount> builder)
    {
        builder.ToTable("external_auth_accounts");

        builder.HasKey(x => x.ExternalAuthAccountId);

        builder.Property(x => x.ExternalAuthAccountId)
            .HasColumnName("external_auth_account_id");

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ProviderUserId)
            .HasColumnName("provider_user_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ProviderUsername)
            .HasColumnName("provider_username")
            .HasMaxLength(255);

        builder.Property(x => x.ProviderEmail)
            .HasColumnName("provider_email")
            .HasMaxLength(255);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(x => new
        {
            x.Provider,
            x.ProviderUserId
        })
        .IsUnique()
        .HasDatabaseName(
            "ux_external_auth_accounts_provider_user");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName(
                "ix_external_auth_accounts_user_id");

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExternalAuthAccounts)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}