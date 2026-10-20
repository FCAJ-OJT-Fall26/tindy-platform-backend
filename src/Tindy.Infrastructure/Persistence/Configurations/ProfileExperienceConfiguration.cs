using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProfileExperienceConfiguration : IEntityTypeConfiguration<ProfileExperience>
{
    public void Configure(EntityTypeBuilder<ProfileExperience> builder)
    {
        builder.ToTable("profile_experiences", table =>
        {
            table.HasCheckConstraint("ck_profile_experiences_dates_order", "start_date IS NULL OR end_date IS NULL OR end_date >= start_date");
            table.HasCheckConstraint("ck_profile_experiences_ongoing_end_date", "NOT is_ongoing OR end_date IS NULL");
            table.HasCheckConstraint("ck_profile_experiences_month_start", "start_date IS NULL OR EXTRACT(DAY FROM start_date) = 1");
            table.HasCheckConstraint("ck_profile_experiences_month_end", "end_date IS NULL OR EXTRACT(DAY FROM end_date) = 1");
        });

        builder.HasKey(x => x.ExperienceId);
        builder.Property(x => x.ExperienceId).HasColumnName("experience_id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ProjectName).HasColumnName("project_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.RoleTitle).HasColumnName("role_title").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text").IsRequired();
        builder.Property(x => x.RepoUrl).HasColumnName("repo_url").HasMaxLength(1000);
        builder.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(x => x.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(x => x.IsOngoing).HasColumnName("is_ongoing").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.User).WithMany(x => x.ProfileExperiences)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.StartDate })
            .HasDatabaseName("ix_profile_experiences_user_id_start_date");
    }
}
