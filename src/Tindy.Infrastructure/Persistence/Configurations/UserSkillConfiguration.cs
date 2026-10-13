
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class UserSkillConfiguration
    : IEntityTypeConfiguration<UserSkill>
{
    public void Configure(EntityTypeBuilder<UserSkill> builder)
    {
        builder.ToTable("user_skills");

        builder.HasKey(x => new
        {
            x.UserId,
            x.SkillId
        });

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.SkillId)
            .HasColumnName("skill_id");

        builder.HasOne(x => x.UserProfile)
            .WithMany(x => x.UserSkills)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Skill)
            .WithMany(x => x.UserSkills)
            .HasForeignKey(x => x.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SkillId)
            .HasDatabaseName("ix_user_skills_skill_id");
    }
}
