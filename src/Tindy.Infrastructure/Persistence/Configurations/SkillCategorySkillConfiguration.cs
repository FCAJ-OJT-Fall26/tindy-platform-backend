using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class SkillCategorySkillConfiguration
    : IEntityTypeConfiguration<SkillCategorySkill>
{
    public void Configure(EntityTypeBuilder<SkillCategorySkill> builder)
    {
        builder.ToTable("skill_category_skills");

        builder.HasKey(x => new { x.CategoryId, x.SkillId });

        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id");

        builder.Property(x => x.SkillId)
            .HasColumnName("skill_id");

        builder.HasOne(x => x.SkillCategory)
            .WithMany(x => x.SkillCategorySkills)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Skill)
            .WithMany(x => x.SkillCategorySkills)
            .HasForeignKey(x => x.SkillId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.SkillId)
            .HasDatabaseName("ix_skill_category_skills_skill_id");
    }
}
