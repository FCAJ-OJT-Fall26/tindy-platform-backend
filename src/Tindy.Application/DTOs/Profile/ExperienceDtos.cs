namespace Tindy.Application.DTOs.Profile;

// This command is produced by ExperienceRequestParser at the HTTP boundary.
// ExperienceService revalidates it, including when called directly from tests or jobs.
public sealed record ExperienceUpsertRequest(
    string ProjectName,
    string RoleTitle,
    string Description,
    string? RepoUrl,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsOngoing,
    IReadOnlyList<Guid> SkillIds);

public sealed record ExperienceTechnologyResponse(Guid SkillId, string Name);

public sealed record ExperienceResponse(
    Guid ExperienceId,
    string ProjectName,
    string RoleTitle,
    string Description,
    IReadOnlyList<ExperienceTechnologyResponse> Technologies,
    string? RepoUrl,
    string? StartDate,
    string? EndDate,
    bool IsOngoing,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
