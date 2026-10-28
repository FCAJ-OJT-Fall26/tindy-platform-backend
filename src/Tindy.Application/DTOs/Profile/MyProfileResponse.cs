namespace Tindy.Application.DTOs.Profile;

// DTO returned only to the profile owner. Do not reuse for member-visible profiles.
public sealed record PreferredRoleSummary(Guid ProfessionalRoleId, string Name);

public sealed record ProfileSkillSummary(Guid SkillId, string Name);

public sealed record MyProfileResponse(
    Guid UserId,
    string? Username,
    string DisplayName,
    string? University,
    string? PhoneNumber,
    string? Headline,
    string? Bio,
    string? Location,
    string? CurrentRole,
    PreferredRoleSummary? PreferredRole,
    int? AvailableHoursPerWeek,
    string? ProfessionalInterests,
    string? LearningGoals,
    string? PreviousProjectsAndContribution,
    string? TechnologiesToLearn,
    string? GithubUrl,
    string? LinkedinUrl,
    string? PersonalWebsiteUrl,
    string? ResumeUrl,
    string? PublicEmail,
    string? AvatarUrl,
    string? CoverImageUrl,
    IReadOnlyList<ProfileSkillSummary> Skills,
    int ExperiencesCount,
    int CertificatesCount,
    string? AvatarSource = null,
    Guid? AvatarMediaAssetId = null,
    Guid? CoverMediaAssetId = null);
