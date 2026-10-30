namespace Tindy.Application.DTOs.Projects;

public sealed record CreateProjectPositionSkillRequest(
    Guid SkillId,
    bool IsRequired = true);

public sealed record CreateProjectPositionRequest(
    Guid RoleId,
    string Title,
    int RequiredCount,
    string ExperienceLevel,
    IReadOnlyList<CreateProjectPositionSkillRequest> Skills,
    string? Description = null);

public sealed record CreateProjectRequest(
    string Name,
    string? Tagline,
    string Description,
    string Category,
    string DifficultyLevel,
    int ExpectedCommitmentHours,
    string Duration,
    int InitialTeamSize,
    int MaxTeamCapacity,
    string? ColorTheme,
    IReadOnlyList<string> Goals,
    IReadOnlyList<CreateProjectPositionRequest> Positions,
    DateOnly? RecruitmentClosingDate = null);

public sealed record ProjectLeaderSummaryDto(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    string? Initials);

public sealed record CreateProjectResponse(
    Guid ProjectId,
    string Name,
    string? Tagline,
    string Category,
    string Status,
    ProjectLeaderSummaryDto Leader,
    int CurrentTeamSize,
    int MaxTeamCapacity,
    int PositionsCount,
    DateTime CreatedAt,
    DateOnly RecruitmentClosingDate,
    DateTime RecruitmentClosesAt,
    bool IsRecruitmentOpen,
    int ClosingDaysLeft);

public sealed record OpenPositionSummaryDto(
    Guid PositionId,
    string Title,
    string RoleName,
    int RequiredCount,
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills);

public sealed record ProjectSummaryDto(
    Guid ProjectId,
    string Name,
    string? Tagline,
    string Description,
    string Category,
    string DifficultyLevel,
    int ExpectedCommitmentHours,
    string Duration,
    int CurrentTeamSize,
    int MaxTeamCapacity,
    string? ColorTheme,
    string? Initials,
    ProjectLeaderSummaryDto Leader,
    IReadOnlyList<OpenPositionSummaryDto> OpenPositions,
    DateTime CreatedAt,
    int? ClosingDaysLeft,
    DateOnly RecruitmentClosingDate,
    DateTime RecruitmentClosesAt,
    bool IsRecruitmentOpen);

public sealed record PaginationMeta(
    int CurrentPage,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record PagedProjectsResponse(
    IReadOnlyList<ProjectSummaryDto> Items,
    PaginationMeta Pagination);

public sealed record ProjectTeamMemberDto(
    Guid UserId,
    string DisplayName,
    string RoleTitle,
    string? AvatarUrl,
    string? Initials,
    bool IsLeader,
    DateTime JoinedAt);

public sealed record PositionSkillDto(
    Guid SkillId,
    string Name,
    bool IsRequired);

public sealed record ProjectPositionDto(
    Guid PositionId,
    Guid ProjectId,
    Guid RoleId,
    string Title,
    string? Description,
    int RequiredCount,
    int FilledCount,
    string ExperienceLevel,
    string Status,
    IReadOnlyList<PositionSkillDto> Skills);

public sealed record ProjectDetailDto(
    Guid ProjectId,
    string Name,
    string? Tagline,
    string Description,
    string Category,
    string DifficultyLevel,
    string Status,
    int ExpectedCommitmentHours,
    string Duration,
    int CurrentTeamSize,
    int MaxTeamCapacity,
    string? ColorTheme,
    string? Initials,
    DateTime CreatedAt,
    int? ClosingDaysLeft,
    ProjectLeaderSummaryDto Leader,
    IReadOnlyList<string> Goals,
    IReadOnlyList<ProjectTeamMemberDto> TeamMembers,
    IReadOnlyList<ProjectPositionDto> Positions,
    DateOnly RecruitmentClosingDate,
    DateTime RecruitmentClosesAt,
    bool IsRecruitmentOpen);

public sealed record UpdateProjectRequest(
    string Name,
    string? Tagline,
    string Description,
    string Category,
    string DifficultyLevel,
    int ExpectedCommitmentHours,
    string Duration,
    int MaxTeamCapacity,
    string? ColorTheme,
    IReadOnlyList<string> Goals,
    DateOnly? RecruitmentClosingDate = null);

public sealed record MyLeadingProjectDto(
    Guid ProjectId,
    string Name,
    string Status,
    int CurrentTeamSize,
    int MaxTeamCapacity,
    int PendingApplicationsCount,
    DateTime CreatedAt);

public sealed record MyJoinedProjectDto(
    Guid ProjectId,
    string Name,
    string Status,
    string MyRole,
    string LeaderName,
    DateTime JoinedAt);

public sealed record MyProjectsResponse(
    IReadOnlyList<MyLeadingProjectDto> LeadingProjects,
    IReadOnlyList<MyJoinedProjectDto> JoinedProjects);

public sealed record GetProjectsQuery(
    string? Q = null,
    string? Category = null,
    string? Role = null,
    string? Skill = null,
    string? Status = "recruiting",
    int Page = 1,
    int PageSize = 10,
    string? SortBy = "newest");

public sealed record CreatePositionRequest(
    Guid RoleId,
    string Title,
    int RequiredCount = 1,
    string? ExperienceLevel = "intermediate",
    string? Description = null);

public sealed record UpdatePositionRequest(
    string? Title,
    int? RequiredCount,
    string? ExperienceLevel,
    string? Status,
    string? Description);

public sealed record AddPositionSkillRequest(
    Guid SkillId,
    bool IsRequired = true,
    string? MinimumExperienceLevel = null);

public sealed record AddPositionSkillResponse(
    Guid PositionId,
    Guid SkillId,
    string SkillName,
    bool IsRequired);

