using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", t => t.HasCheckConstraint("ck_notifications_type",
            "type IN ('invitation_received','invitation_accepted','invitation_declined','invitation_cancelled','application_nudged')"));
        b.HasKey(x => x.NotificationId);
        b.Property(x => x.NotificationId).HasColumnName("notification_id").ValueGeneratedNever();
        b.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id");
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.InvitationId).HasColumnName("invitation_id").IsRequired(false);
        b.Property(x => x.ApplicationId).HasColumnName("application_id").IsRequired(false);
        b.Property(x => x.Type).HasColumnName("type").HasMaxLength(60).IsRequired();
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ReadAt).HasColumnName("read_at").HasColumnType("timestamp with time zone");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        b.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Invitation).WithMany().HasForeignKey(x => x.InvitationId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.InvitationId, x.Type, x.RecipientUserId }).IsUnique().HasFilter("invitation_id IS NOT NULL").HasDatabaseName("ux_notifications_invitation_event_recipient");
        b.HasIndex(x => new { x.ApplicationId, x.Type, x.RecipientUserId }).IsUnique().HasFilter("application_id IS NOT NULL").HasDatabaseName("ux_notifications_application_event_recipient");
        b.HasIndex(x => new { x.RecipientUserId, x.CreatedAt, x.NotificationId }).IsDescending(false, true, true).HasDatabaseName("ix_notifications_recipient_history");
        b.HasIndex(x => x.RecipientUserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_recipient_unread");
        b.HasIndex(x => x.ActorUserId).HasDatabaseName("ix_notifications_actor");
    }
}
