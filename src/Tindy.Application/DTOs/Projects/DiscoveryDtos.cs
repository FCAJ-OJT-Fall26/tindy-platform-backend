namespace Tindy.Application.DTOs.Projects;

public sealed record DiscoveryFilters(string? Category = null, Guid? RoleId = null, Guid? SkillId = null);

// Read projections whitelist only the data needed by discovery and scoring.
public sealed record DiscoveryMemberProfile(Guid? PreferredRoleId, int? AvailableHoursPerWeek, IReadOnlyList<Guid> SkillIds);
public sealed record DiscoverySkill(Guid SkillId, string Name, bool IsRequired);
public sealed record DiscoveryPosition(Guid PositionId, string Title, Guid RoleId, string RoleName,
    string ExperienceLevel, int RequiredCount, int FilledCount, string Status, IReadOnlyList<DiscoverySkill> Skills);
public sealed record DiscoveryProject(Guid ProjectId, string Name, string? Tagline, string Description, string Category,
    string DifficultyLevel, int ExpectedCommitmentHours, string Duration, string? ColorTheme,
    string Status, DateTime? ArchivedAt, DateTime CreatedAt, DateTime RecruitmentClosesAt,
    int CurrentTeamSize, int MaxTeamCapacity, ProjectLeaderSummaryDto Leader, IReadOnlyList<DiscoveryPosition> Positions, bool IsSaved = false);

public sealed record MatchFactor(string Name, decimal? Factor, decimal NormalizedWeight, decimal Contribution);
public sealed record MatchPositionSummary(Guid PositionId, string Title, Guid RoleId, string RoleName, string ExperienceLevel,
    int RequiredCount, int FilledCount, int RemainingSlots);
public sealed record PositionMatch(int MatchScore, string AlgorithmVersion, MatchPositionSummary BestPosition,
    IReadOnlyList<MatchFactor> MatchFactors, IReadOnlyList<DiscoverySkill> MatchedSkills,
    IReadOnlyList<DiscoverySkill> MissingSkills, bool DataIncomplete, string Explanation);

public sealed record DiscoveryCard(Guid ProjectId, string Name, string? Tagline, string Description, string Category,
    string DifficultyLevel, int ExpectedCommitmentHours, string Duration, string? ColorTheme,
    int CurrentTeamSize, int MaxTeamCapacity, ProjectLeaderSummaryDto Leader, DateTime CreatedAt,
    DateOnly RecruitmentClosingDate, DateTime RecruitmentClosesAt, bool IsRecruitmentOpen, int ClosingDaysLeft,
    int MatchScore, string AlgorithmVersion, MatchPositionSummary BestPosition, IReadOnlyList<MatchFactor> MatchFactors,
    IReadOnlyList<DiscoverySkill> MatchedSkills, IReadOnlyList<DiscoverySkill> MissingSkills, bool DataIncomplete,
    string Explanation, string FeedBucket, bool IsSaved);
public sealed record DiscoveryPage(IReadOnlyList<DiscoveryCard> Items, string? NextCursor, bool HasMore, string FeedVersion = "feed-v1");
public sealed record SavedDiscoveryProject(Guid ProjectId, string Name, string? Tagline, string Category, string Status,
    DateTime RecruitmentClosesAt, bool IsRecruitmentOpen, bool IsSkipped, DateTime UpdatedAt);
public sealed record SavedDiscoveryPage(IReadOnlyList<SavedDiscoveryProject> Items, PaginationMeta Pagination);
