using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Repositories;

public interface ITeammateDiscoveryInteractionRepository
{
    Task<TeammateInteractionsPage> GetAsync(Guid leaderId, Guid projectId, Guid positionId, string? action, int page, int pageSize, CancellationToken ct);
    Task<TeammateInteractionDto> SetAsync(Guid leaderId, Guid projectId, Guid positionId, Guid candidateId, string action, CancellationToken ct);
    Task ResetAsync(Guid leaderId, Guid projectId, Guid positionId, Guid candidateId, CancellationToken ct);
}
