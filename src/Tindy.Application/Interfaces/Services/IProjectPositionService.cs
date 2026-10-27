using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Services;

public interface IProjectPositionService
{
    Task<IReadOnlyList<ProjectPositionDto>> GetPositionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectPositionDto> CreatePositionAsync(Guid currentUserId, Guid projectId, CreatePositionRequest request, CancellationToken cancellationToken = default);
    Task<ProjectPositionDto> UpdatePositionAsync(Guid currentUserId, Guid projectId, Guid positionId, UpdatePositionRequest request, CancellationToken cancellationToken = default);
    Task DeletePositionAsync(Guid currentUserId, Guid projectId, Guid positionId, CancellationToken cancellationToken = default);
    Task<AddPositionSkillResponse> AddPositionSkillAsync(Guid currentUserId, Guid projectId, Guid positionId, AddPositionSkillRequest request, CancellationToken cancellationToken = default);
    Task RemovePositionSkillAsync(Guid currentUserId, Guid projectId, Guid positionId, Guid skillId, CancellationToken cancellationToken = default);
}
