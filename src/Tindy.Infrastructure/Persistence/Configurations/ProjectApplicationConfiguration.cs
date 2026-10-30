using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectApplicationConfiguration : IEntityTypeConfiguration<ProjectApplication>
{
    public void Configure(EntityTypeBuilder<ProjectApplication> builder)
    {
        builder.ToTable("project_applications", t => t.HasCheckConstraint("ck_project_applications_status", "status IN ('pending','accepted','rejected','withdrawn')"));
        builder.HasKey(a => a.ApplicationId);
        builder.Property(a => a.ApplicationId).HasColumnName("application_id").ValueGeneratedNever();
        builder.Property(a => a.ProjectId).HasColumnName("project_id");
        builder.Property(a => a.PositionId).HasColumnName("position_id");
        builder.Property(a => a.ApplicantId).HasColumnName("applicant_id");
        builder.Property(a => a.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(a => a.Message).HasColumnName("message").HasMaxLength(2000);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(a => a.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamp with time zone");
        builder.Property(a => a.ReviewedBy).HasColumnName("reviewed_by");
        builder.HasOne(a => a.Project).WithMany(p => p.Applications).HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Position).WithMany().HasForeignKey(a => new { a.PositionId, a.ProjectId })
            .HasPrincipalKey(p => new { p.PositionId, p.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Applicant).WithMany().HasForeignKey(a => a.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Reviewer).WithMany().HasForeignKey(a => a.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.ProjectId, a.ApplicantId }).IsUnique().HasFilter("status = 'pending'")
            .HasDatabaseName("ux_project_applications_pending");
        builder.HasIndex(a => new { a.ProjectId, a.Status, a.CreatedAt }).HasDatabaseName("ix_project_applications_review");
        builder.HasIndex(a => new { a.ApplicantId, a.CreatedAt }).HasDatabaseName("ix_project_applications_applicant");
    }
}
