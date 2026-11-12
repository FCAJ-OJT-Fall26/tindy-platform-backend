namespace Tindy.Application.DTOs.Projects;

public sealed record ProjectGoalDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Tag,
    string DueDate,
    bool IsCompleted,
    IReadOnlyList<Guid> AssigneeIds,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CreateProjectGoalRequest(
    string Title,
    string? Tag = null,
    string? DueDate = null,
    IReadOnlyList<Guid>? AssigneeIds = null);

public sealed record ToggleProjectGoalRequest(
    bool? IsCompleted = null);

public sealed record ProjectResourceLinkDto(
    string Type,
    string Label,
    string Url);

public sealed record UpdateProjectResourcesRequest(
    IReadOnlyList<ProjectResourceLinkDto> Resources);
