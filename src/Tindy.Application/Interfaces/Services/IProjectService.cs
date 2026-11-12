using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Services;

public interface IProjectService
{
    Task<CreateProjectResponse> CreateProjectAsync(Guid currentUserId, CreateProjectRequest request, CancellationToken cancellationToken = default);
    Task<PagedProjectsResponse> GetProjectsAsync(GetProjectsQuery query, CancellationToken cancellationToken = default);
    Task<ProjectDetailDto> GetProjectByIdAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectDetailDto> UpdateProjectAsync(Guid currentUserId, Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default);
    Task DeleteProjectAsync(Guid currentUserId, Guid projectId, CancellationToken cancellationToken = default);
    Task<MyProjectsResponse> GetMyProjectsAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectGoalDto>> GetProjectGoalsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectGoalDto> CreateProjectGoalAsync(Guid currentUserId, Guid projectId, CreateProjectGoalRequest request, CancellationToken cancellationToken = default);
    Task<ProjectGoalDto> ToggleProjectGoalAsync(Guid currentUserId, Guid projectId, Guid goalId, ToggleProjectGoalRequest? request = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectResourceLinkDto>> UpdateProjectResourcesAsync(Guid currentUserId, Guid projectId, UpdateProjectResourcesRequest request, CancellationToken cancellationToken = default);
    Task<ProjectDetailDto> CompleteProjectAsync(Guid currentUserId, Guid projectId, CancellationToken cancellationToken = default);
}
