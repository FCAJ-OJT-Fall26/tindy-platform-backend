using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectPositionSkillConfiguration : IEntityTypeConfiguration<ProjectPositionSkill>
{
    public void Configure(EntityTypeBuilder<ProjectPositionSkill> builder)
    {
        builder.ToTable("position_skills");

        builder.HasKey(x => new { x.PositionId, x.SkillId });
        builder.Property(x => x.PositionId).HasColumnName("position_id");
        builder.Property(x => x.SkillId).HasColumnName("skill_id");
        builder.Property(x => x.IsRequired).HasColumnName("is_required").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.MinimumExperienceLevel).HasColumnName("minimum_experience_level").HasMaxLength(50);

        builder.HasOne(x => x.Position).WithMany(x => x.PositionSkills)
            .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Skill).WithMany(x => x.PositionSkills)
            .HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SkillId).HasDatabaseName("ix_position_skills_skill_id");
    }
}
