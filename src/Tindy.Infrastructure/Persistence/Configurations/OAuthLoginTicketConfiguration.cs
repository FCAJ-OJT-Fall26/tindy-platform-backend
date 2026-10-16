using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Infrastructure.Persistence.Models;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class OAuthLoginTicketConfiguration : IEntityTypeConfiguration<OAuthLoginTicket>
{
    public void Configure(EntityTypeBuilder<OAuthLoginTicket> builder)
    {
        builder.ToTable("oauth_login_tickets");
        builder.HasKey(x => x.TicketHash);
        builder.Property(x => x.TicketHash).HasColumnName("ticket_hash").HasMaxLength(64);
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_oauth_login_tickets_expires_at");
        builder.HasOne<Tindy.Application.Entities.User>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
