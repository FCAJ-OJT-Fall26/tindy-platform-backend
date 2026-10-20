using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Models;
using Xunit;

namespace Tindy.Tests.Profile;

/// <summary>
/// Verify the EF model without opening a PostgreSQL connection.
/// This does not validate generated SQL, applied migrations or live database constraints.
/// </summary>
public sealed class ProfileSchemaMetadataRegressionTests
{
    private static TindyDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only_not_connected;Username=none;Password=none")
            .Options;
        return new TindyDbContext(options);
    }

    [Fact]
    public void ProfileModel_HasNoVisibilityFlag_AndRetainsOptionalAvatar()
    {
        using var context = NewContext();
        var entity = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IEntityType>(
            context.Model.FindEntityType(typeof(UserProfile)));
        Assert.Null(entity.FindProperty("IsPublic"));
        Assert.NotNull(entity.FindProperty("AvatarUrl"));
        Assert.NotNull(entity.FindProperty("CoverImageUrl"));
    }

    [Fact]
    public void Experiences_HaveOwnerForeignKey_MonthDates_AndCascadeLinks()
    {
        using var context = NewContext();
        var experience = context.Model.FindEntityType(typeof(ProfileExperience))!;
        var link = context.Model.FindEntityType(typeof(ProfileExperienceSkill))!;
        Assert.Equal("profile_experiences", experience.GetTableName());
        Assert.Equal("profile_experience_skills", link.GetTableName());
        Assert.Equal("date", experience.FindProperty(nameof(ProfileExperience.StartDate))!.GetColumnType());
        Assert.Equal("date", experience.FindProperty(nameof(ProfileExperience.EndDate))!.GetColumnType());
        Assert.Equal(150, experience.FindProperty(nameof(ProfileExperience.ProjectName))!.GetMaxLength());
        Assert.Equal(new[] { nameof(ProfileExperienceSkill.ExperienceId), nameof(ProfileExperienceSkill.SkillId) },
            link.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(DeleteBehavior.Cascade, link.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(ProfileExperience)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, link.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Skill)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, experience.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(User)).DeleteBehavior);
    }

    [Fact]
    public void Certificates_HaveOwnerForeignKey_AndNativeDateColumns()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(UserCertificate))!;
        Assert.Equal("user_certificates", entity.GetTableName());
        Assert.Equal("date", entity.FindProperty(nameof(UserCertificate.IssuedAt))!.GetColumnType());
        Assert.Equal("date", entity.FindProperty(nameof(UserCertificate.ExpiresAt))!.GetColumnType());
        Assert.Equal(255, entity.FindProperty(nameof(UserCertificate.Title))!.GetMaxLength());
        Assert.Equal(1000, entity.FindProperty(nameof(UserCertificate.CredentialUrl))!.GetMaxLength());
        Assert.Equal(DeleteBehavior.Cascade, entity.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(User)).DeleteBehavior);
        Assert.Null(entity.FindProperty("VerificationStatus"));
        Assert.Null(entity.FindProperty("IsVerified"));
    }

    [Fact]
    public void OAuthLinkTicket_PreservesNullableAvatarForConfirmation()
    {
        using var context = NewContext();
        var transaction = context.Model.FindEntityType(typeof(OAuthLinkTransaction))!;
        var avatar = transaction.FindProperty(nameof(OAuthLinkTransaction.AvatarUrl));
        Assert.NotNull(avatar);
        Assert.True(avatar!.IsNullable);
        Assert.Equal(1000, avatar.GetMaxLength());
    }
}
