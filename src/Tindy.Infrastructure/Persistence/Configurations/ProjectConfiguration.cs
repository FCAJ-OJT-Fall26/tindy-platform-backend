using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", table =>
        {
            table.HasCheckConstraint("ck_projects_hours_range", "expected_commitment_hours >= 1 AND expected_commitment_hours <= 40");
            table.HasCheckConstraint("ck_projects_team_capacity", "max_team_capacity >= initial_team_size");
        });

        builder.HasKey(x => x.ProjectId);
        builder.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
        builder.Property(x => x.LeaderId).HasColumnName("leader_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Tagline).HasColumnName("tagline").HasMaxLength(255);
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DifficultyLevel).HasColumnName("difficulty_level").HasMaxLength(50).HasDefaultValue("intermediate").IsRequired();
        builder.Property(x => x.ExpectedCommitmentHours).HasColumnName("expected_commitment_hours").HasDefaultValue(10).IsRequired();
        builder.Property(x => x.Duration).HasColumnName("duration").HasMaxLength(50).HasDefaultValue("3 months").IsRequired();
        builder.Property(x => x.InitialTeamSize).HasColumnName("initial_team_size").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.MaxTeamCapacity).HasColumnName("max_team_capacity").HasDefaultValue(5).IsRequired();
        builder.Property(x => x.ColorTheme).HasColumnName("color_theme").HasMaxLength(50).HasDefaultValue("indigo");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).HasDefaultValue("recruiting").IsRequired();
        builder.Property(x => x.RecruitmentClosesAt).HasColumnName("recruitment_closes_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        // Retained only for safe migration/rollback during client rollout. No
        // application code reads this legacy shadow column as the deadline.
        builder.Property<int>("RecruitmentClosingDays").HasColumnName("recruitment_closing_days")
            .HasDefaultValue(14).IsRequired();

        // Goals stored as jsonb in PostgreSQL
        builder.Property(x => x.Goals).HasColumnName("goals").HasColumnType("jsonb");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ArchivedAt).HasColumnName("archived_at").HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Leader).WithMany(x => x.LedProjects)
            .HasForeignKey(x => x.LeaderId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.LeaderId).HasDatabaseName("ix_projects_leader_id");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_projects_status");
        builder.HasIndex(x => x.Category).HasDatabaseName("ix_projects_category");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_projects_created_at");
        builder.HasIndex(x => x.RecruitmentClosesAt).HasDatabaseName("ix_projects_recruitment_closes_at");
    }
}
