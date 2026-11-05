using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Repositories;

public interface ITeammateDiscoveryRepository
{
    Task<TeammateDiscoveryContext> GetContextAsync(Guid leaderId, Guid projectId, Guid positionId, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<TeammateCandidateData>> GetCandidatesAsync(Guid projectId, Guid positionId, DateTime now, TeammateDiscoveryFilters filters, CancellationToken ct);
}
