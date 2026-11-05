namespace Tindy.Application.DTOs.Projects;

public sealed record TeammateDiscoveryFilters(Guid? RoleId = null, Guid? SkillId = null, int? MinAvailableHoursPerWeek = null, string SortBy = "match");
public sealed record TeammateCandidatePreview(Guid UserId, string DisplayName, string? Headline, string? AvatarUrl,
    Guid? PreferredRoleId, string? PreferredRoleName, int? AvailableHoursPerWeek, IReadOnlyList<ApplicantSkill> Skills,
    int ExperiencesCount, int CertificatesCount, string ProfileUrl, string ExperiencesUrl, string CertificatesUrl);
public sealed record TeammateCandidateData(TeammateCandidatePreview Candidate, string? InteractionAction);
public sealed record TeammateCandidateCard(TeammateCandidatePreview Candidate, string? InteractionAction, PositionMatch Matching);
public sealed record TeammateDiscoveryContext(Guid ProjectId, DiscoveryPosition Position, int ExpectedCommitmentHours);
public sealed record TeammateDiscoveryPage(Guid ProjectId, Guid PositionId, DateTime ScoredAt,
    IReadOnlyList<TeammateCandidateCard> Items, string? NextCursor, bool HasMore, string FeedVersion = "teammates-v1");
