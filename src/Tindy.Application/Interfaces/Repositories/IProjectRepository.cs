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
    Task<IReadOnlyList<ProjectPosition>> GetPositionsByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectPosition?> GetPositionWithDetailsAsync(Guid projectId, Guid positionId, CancellationToken cancellationToken = default);
    Task<ProjectPosition> AddPositionAsync(ProjectPosition position, CancellationToken cancellationToken = default);
    Task UpdatePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default);
    Task DeletePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default);
    Task<Skill?> GetSkillByIdAsync(Guid skillId, CancellationToken cancellationToken = default);
    Task<ProjectPositionSkill?> GetPositionSkillAsync(Guid positionId, Guid skillId, CancellationToken cancellationToken = default);
    Task AddPositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default);
    Task DeletePositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default);
}
