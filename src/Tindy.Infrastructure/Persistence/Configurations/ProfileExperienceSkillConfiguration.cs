using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProfileExperienceSkillConfiguration : IEntityTypeConfiguration<ProfileExperienceSkill>
{
    public void Configure(EntityTypeBuilder<ProfileExperienceSkill> builder)
    {
        builder.ToTable("profile_experience_skills");
        builder.HasKey(x => new { x.ExperienceId, x.SkillId });
        builder.Property(x => x.ExperienceId).HasColumnName("experience_id");
        builder.Property(x => x.SkillId).HasColumnName("skill_id");

        builder.HasOne(x => x.Experience).WithMany(x => x.ExperienceSkills)
            .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Skill).WithMany(x => x.ProfileExperienceSkills)
            .HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SkillId).HasDatabaseName("ix_profile_experience_skills_skill_id");
    }
}
