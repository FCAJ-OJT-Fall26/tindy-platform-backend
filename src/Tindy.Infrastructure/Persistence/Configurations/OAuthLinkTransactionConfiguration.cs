using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Infrastructure.Persistence.Models;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class OAuthLinkTransactionConfiguration : IEntityTypeConfiguration<OAuthLinkTransaction>
{
    public void Configure(EntityTypeBuilder<OAuthLinkTransaction> builder)
    {
        builder.ToTable("oauth_link_transactions");
        builder.HasKey(x => x.TransactionId);
        builder.Property(x => x.TransactionId).HasColumnName("transaction_id");
        builder.Property(x => x.StartTicketHash).HasColumnName("start_ticket_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CompletionTicketHash).HasColumnName("completion_ticket_hash").HasMaxLength(64);
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProviderUserId).HasColumnName("provider_user_id").HasMaxLength(255);
        builder.Property(x => x.ProviderUsername).HasColumnName("provider_username").HasMaxLength(255);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        builder.Property(x => x.ProviderEmail).HasColumnName("provider_email").HasMaxLength(255);
        builder.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(1000);
        builder.Property(x => x.EmailVerified).HasColumnName("email_verified").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.HasIndex(x => x.StartTicketHash).IsUnique().HasDatabaseName("ux_oauth_link_transactions_start_hash");
        builder.HasIndex(x => x.CompletionTicketHash).IsUnique().HasDatabaseName("ux_oauth_link_transactions_completion_hash");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_oauth_link_transactions_expires_at");
        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_oauth_link_transactions_user_id");
        builder.HasOne<Tindy.Application.Entities.User>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
