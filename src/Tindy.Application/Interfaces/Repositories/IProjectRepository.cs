using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<Project?> GetByIdWithDetailsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default);
    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Project> Items, int TotalCount)> GetPagedProjectsAsync(GetProjectsQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetLeadingProjectsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectMember>> GetJoinedMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> RoleExistsAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ValidateSkillIdsAsync(IEnumerable<Guid> skillIds, CancellationToken cancellationToken = default);
}
