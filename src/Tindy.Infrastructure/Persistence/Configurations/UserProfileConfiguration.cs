using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class UserProfileConfiguration
    : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20);

        builder.Property(x => x.Headline)
            .HasColumnName("headline")
            .HasMaxLength(255);

        builder.Property(x => x.Bio)
            .HasColumnName("bio")
            .HasColumnType("text");

        builder.Property(x => x.Location)
            .HasColumnName("location")
            .HasMaxLength(255);

        builder.Property(x => x.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(1000);

        builder.Property(x => x.CoverImageUrl)
            .HasColumnName("cover_image_url")
            .HasMaxLength(1000);

        builder.Property(x => x.CareerGoal)
            .HasColumnName("career_goal")
            .HasMaxLength(500);

        builder.Property(x => x.CurrentRole)
            .HasColumnName("current_role")
            .HasMaxLength(255);

        builder.Property(x => x.PublicEmail)
            .HasColumnName("public_email")
            .HasMaxLength(255);

        builder.Property(x => x.GithubUrl)
            .HasColumnName("github_url")
            .HasMaxLength(1000);

        builder.Property(x => x.LinkedinUrl)
            .HasColumnName("linkedin_url")
            .HasMaxLength(1000);

        builder.Property(x => x.ResumeUrl)
            .HasColumnName("resume_url")
            .HasMaxLength(1000);

        builder.Property(x => x.PersonalWebsiteUrl)
            .HasColumnName("personal_website_url")
            .HasMaxLength(1000);

        builder.Property(x => x.IsPublic)
            .HasColumnName("is_public")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasOne(x => x.User)
            .WithOne(x => x.Profile)
            .HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}