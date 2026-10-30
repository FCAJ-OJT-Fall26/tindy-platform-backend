using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class DiscoveryProjectInteractionConfiguration : IEntityTypeConfiguration<DiscoveryProjectInteraction>
{
    public void Configure(EntityTypeBuilder<DiscoveryProjectInteraction> builder)
    {
        builder.ToTable("discovery_project_interactions");
        builder.HasKey(i => new { i.UserId, i.ProjectId });
        builder.Property(i => i.UserId).HasColumnName("user_id");
        builder.Property(i => i.ProjectId).HasColumnName("project_id");
        builder.Property(i => i.IsSaved).HasColumnName("is_saved");
        builder.Property(i => i.IsSkipped).HasColumnName("is_skipped");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Project).WithMany(p => p.DiscoveryInteractions).HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.UserId, i.UpdatedAt }).HasFilter("is_saved = TRUE").HasDatabaseName("ix_discovery_interactions_saved");
    }
}
