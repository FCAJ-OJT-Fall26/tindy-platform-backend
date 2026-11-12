using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectGoalConfiguration : IEntityTypeConfiguration<ProjectGoal>
{
    public void Configure(EntityTypeBuilder<ProjectGoal> builder)
    {
        builder.ToTable("project_goals");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Tag).HasColumnName("tag").HasMaxLength(100).HasDefaultValue("Milestone").IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date").HasMaxLength(100).HasDefaultValue("TBD").IsRequired();
        builder.Property(x => x.IsCompleted).HasColumnName("is_completed").HasDefaultValue(false).IsRequired();

        var jsonOptions = new JsonSerializerOptions();
        builder.Property(x => x.AssigneeIds)
            .HasColumnName("assignee_ids")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, jsonOptions) ?? new List<Guid>(),
                new ValueComparer<List<Guid>>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()))
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Project)
            .WithMany(x => x.WorkspaceGoals)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_project_goals_project_id");
    }
}
