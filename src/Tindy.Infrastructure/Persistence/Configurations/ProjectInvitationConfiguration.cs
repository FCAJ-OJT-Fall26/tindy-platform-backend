using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectInvitationConfiguration : IEntityTypeConfiguration<ProjectInvitation>
{
    public void Configure(EntityTypeBuilder<ProjectInvitation> b)
    {
        b.ToTable("project_invitations", t =>
        {
            t.HasCheckConstraint("ck_project_invitations_status", "status IN ('pending','accepted','declined','cancelled','expired')");
            t.HasCheckConstraint("ck_project_invitations_distinct_users", "invited_user_id <> inviting_leader_id");
            t.HasCheckConstraint("ck_project_invitations_expiry", "expires_at = created_at + interval '168 hours'");
            t.HasCheckConstraint("ck_project_invitations_timestamps", "updated_at >= created_at AND (" +
                "(status IN ('accepted','declined') AND responded_at IS NOT NULL AND responded_at >= created_at AND responded_at < expires_at AND updated_at = responded_at) OR " +
                "(status = 'pending' AND responded_at IS NULL AND updated_at = created_at) OR " +
                "(status = 'expired' AND responded_at IS NULL AND updated_at >= expires_at) OR " +
                "(status = 'cancelled' AND responded_at IS NULL AND updated_at < expires_at))");
        });
        b.HasKey(x => x.InvitationId);
        b.Property(x => x.InvitationId).HasColumnName("invitation_id").ValueGeneratedNever();
        b.Property(x => x.ProjectId).HasColumnName("project_id");
        b.Property(x => x.PositionId).HasColumnName("position_id");
        b.Property(x => x.InvitedUserId).HasColumnName("invited_user_id");
        b.Property(x => x.InvitingLeaderId).HasColumnName("inviting_leader_id");
        b.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        b.Property(x => x.RespondedAt).HasColumnName("responded_at").HasColumnType("timestamp with time zone");
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Position).WithMany().HasForeignKey(x => new { x.PositionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.PositionId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.InvitedUser).WithMany().HasForeignKey(x => x.InvitedUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.InvitingLeader).WithMany().HasForeignKey(x => x.InvitingLeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProjectId, x.InvitedUserId }).IsUnique().HasFilter("status = 'pending'").HasDatabaseName("ux_project_invitations_pending");
        b.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.InvitationId }).IsDescending(false, true, false).HasDatabaseName("ix_project_invitations_project_history");
        b.HasIndex(x => new { x.InvitedUserId, x.CreatedAt, x.InvitationId }).IsDescending(false, true, false).HasDatabaseName("ix_project_invitations_member_history");
        b.HasIndex(x => new { x.ProjectId, x.InvitedUserId, x.RespondedAt }).IsDescending(false, false, true).HasFilter("status = 'declined'").HasDatabaseName("ix_project_invitations_declined");
        b.HasIndex(x => new { x.PositionId, x.ProjectId }).HasDatabaseName("ix_project_invitations_position");
        b.HasIndex(x => x.InvitingLeaderId).HasDatabaseName("ix_project_invitations_inviting_leader");
    }
}
