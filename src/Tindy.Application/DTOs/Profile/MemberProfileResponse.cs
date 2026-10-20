namespace Tindy.Application.DTOs.Profile;

// Whitelist DTO for member-visible profiles. Private account/identity/contact
// fields deliberately do not exist in this type, including for self-view.
public sealed record MemberProfileResponse(
    Guid UserId,
    string? Username,
    string DisplayName,
    string? University,
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
    string? PublicEmail,
    string? AvatarUrl,
    string? CoverImageUrl,
    IReadOnlyList<ProfileSkillSummary> Skills,
    int ExperiencesCount,
    int CertificatesCount);
