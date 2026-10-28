namespace Tindy.Application.Entities;

public class UserProfile
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Headline { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverImageUrl { get; set; }

    // Nullable while legacy avatar_url / cover_image_url values are migrated.
    // Null means legacy/unclassified, not "default".
    public string? AvatarSource { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public string? AvatarOAuthProvider { get; set; }
    public Guid? CoverAssetId { get; set; }
    public MediaAsset? AvatarAsset { get; set; }
    public MediaAsset? CoverAsset { get; set; }
    public ExternalAuthAccount? AvatarOAuthAccount { get; set; }
    public string? University { get; set; }
    public int? AvailableHoursPerWeek { get; set; }
    public string? CurrentRole { get; set; }
    public string? PublicEmail { get; set; }
    public string? GithubUrl { get; set; }
    public string? LinkedinUrl { get; set; }
    public string? ResumeUrl { get; set; }
    public string? PersonalWebsiteUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public User User { get; set; } = null!;
    public Guid? PreferredRoleId { get; set; }

    public ProfessionalRole? PreferredProfessionalRole { get; set; }
    public string? ProfessionalInterests { get; set; }
    public string? LearningGoals { get; set; }
    public string? PreviousProjectsAndContribution { get; set; }
    public string? TechnologiesToLearn { get; set; }
    public ICollection<UserSkill> UserSkills { get; set; }
        = new List<UserSkill>();
}