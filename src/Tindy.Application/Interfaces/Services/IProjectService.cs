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
}
