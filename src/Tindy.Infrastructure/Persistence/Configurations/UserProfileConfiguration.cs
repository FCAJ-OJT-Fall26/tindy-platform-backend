using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class UserProfileConfiguration
    : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles", table =>
        {
            table.HasCheckConstraint("ck_user_profiles_available_hours_per_week", "available_hours_per_week IS NULL OR " + "available_hours_per_week BETWEEN 1 AND 40");
            // Leave AvatarSource null for legacy rows until the later media migration step.
            table.HasCheckConstraint("ck_user_profiles_avatar_selection", "(avatar_source IS NULL AND avatar_asset_id IS NULL AND avatar_oauth_provider IS NULL) OR " +
                "(avatar_source = 'default' AND avatar_asset_id IS NULL AND avatar_oauth_provider IS NULL) OR " +
                "(avatar_source = 'uploaded' AND avatar_asset_id IS NOT NULL AND avatar_oauth_provider IS NULL) OR " +
                "(avatar_source = 'oauth' AND avatar_asset_id IS NOT NULL AND avatar_oauth_provider IN ('Google', 'GitHub'))");
        });

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

        builder.Property(x => x.AvatarSource).HasColumnName("avatar_source").HasMaxLength(20);
        builder.Property(x => x.AvatarAssetId).HasColumnName("avatar_asset_id");
        builder.Property(x => x.AvatarOAuthProvider).HasColumnName("avatar_oauth_provider").HasMaxLength(30);
        builder.Property(x => x.CoverAssetId).HasColumnName("cover_asset_id");

        builder.HasOne(x => x.AvatarAsset).WithMany()
            .HasForeignKey(x => new { x.AvatarAssetId, x.UserId })
            .HasPrincipalKey(x => new { x.MediaAssetId, x.OwnerUserId })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.CoverAsset).WithMany()
            .HasForeignKey(x => new { x.CoverAssetId, x.UserId })
            .HasPrincipalKey(x => new { x.MediaAssetId, x.OwnerUserId })
            .OnDelete(DeleteBehavior.NoAction);
        // The provider strings in the existing codebase are "Google" and "GitHub".
        builder.HasOne(x => x.AvatarOAuthAccount).WithMany()
            .HasForeignKey(x => new { x.UserId, x.AvatarOAuthProvider })
            .HasPrincipalKey(x => new { x.UserId, x.Provider })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.AvatarAssetId).HasDatabaseName("ix_user_profiles_avatar_asset_id");
        builder.HasIndex(x => x.CoverAssetId).HasDatabaseName("ix_user_profiles_cover_asset_id");

        builder.Property(x => x.CurrentRole)
            .HasColumnName("current_role")
            .HasMaxLength(255);

        builder.Property(x => x.University)
            .HasColumnName("university")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.AvailableHoursPerWeek)
            .HasColumnName("available_hours_per_week")
            .IsRequired(false);

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

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");


        builder.Property(x => x.PreferredRoleId)
            .HasColumnName("preferred_role_id")
            .IsRequired(false);

        builder.HasOne(x => x.PreferredProfessionalRole)
            .WithMany(x => x.UserProfiles)
            .HasForeignKey(x => x.PreferredRoleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.PreferredRoleId)
            .HasDatabaseName("ix_user_profiles_preferred_role_id");

        builder.Property(x => x.ProfessionalInterests)
            .HasColumnName("professional_interests")
            .HasColumnType("text");

        builder.Property(x => x.LearningGoals)
            .HasColumnName("learning_goals")
            .HasColumnType("text");

        builder.Property(x => x.PreviousProjectsAndContribution)
            .HasColumnName("previous_projects_and_contribution")
            .HasColumnType("text");

        builder.Property(x => x.TechnologiesToLearn)
            .HasColumnName("technologies_to_learn")
            .HasColumnType("text");


        builder.HasOne(x => x.User)
            .WithOne(x => x.Profile)
            .HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}