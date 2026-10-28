using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectPositionConfiguration : IEntityTypeConfiguration<ProjectPosition>
{
    public void Configure(EntityTypeBuilder<ProjectPosition> builder)
    {
        builder.ToTable("project_positions");

        builder.HasKey(x => x.PositionId);
        builder.Property(x => x.PositionId).HasColumnName("position_id").ValueGeneratedNever();
        builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(x => x.RequiredCount).HasColumnName("required_count").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.FilledCount).HasColumnName("filled_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ExperienceLevel).HasColumnName("experience_level").HasMaxLength(50).HasDefaultValue("intermediate").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).HasDefaultValue("open").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Project).WithMany(x => x.Positions)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Role).WithMany(x => x.ProjectPositions)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_project_positions_project_id");
        builder.HasIndex(x => x.RoleId).HasDatabaseName("ix_project_positions_role_id");
    }
}
