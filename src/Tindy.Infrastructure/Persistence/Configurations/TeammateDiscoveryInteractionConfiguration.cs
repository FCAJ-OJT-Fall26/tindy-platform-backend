using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class TeammateDiscoveryInteractionConfiguration : IEntityTypeConfiguration<TeammateDiscoveryInteraction>
{
    public void Configure(EntityTypeBuilder<TeammateDiscoveryInteraction> b)
    {
        b.ToTable("teammate_discovery_interactions", t =>
        {
            t.HasCheckConstraint("ck_teammate_discovery_interactions_action", "action IN ('saved','shortlisted','skipped')");
            t.HasCheckConstraint("ck_teammate_discovery_interactions_timestamps", "updated_at >= created_at");
        });
        b.HasKey(x => new { x.ProjectId, x.PositionId, x.CandidateUserId });
        b.Property(x => x.ProjectId).HasColumnName("project_id");
        b.Property(x => x.PositionId).HasColumnName("position_id");
        b.Property(x => x.CandidateUserId).HasColumnName("candidate_user_id");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Position).WithMany().HasForeignKey(x => new { x.PositionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.PositionId, x.ProjectId }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProjectId, x.PositionId, x.Action, x.UpdatedAt, x.CandidateUserId })
            .IsDescending(false, false, false, true, false).HasDatabaseName("ix_teammate_interactions_list");
        b.HasIndex(x => x.CandidateUserId).HasDatabaseName("ix_teammate_interactions_candidate");
    }
}
