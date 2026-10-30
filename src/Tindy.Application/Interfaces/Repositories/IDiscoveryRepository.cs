using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Repositories;

public interface IDiscoveryRepository
{
    Task<DiscoveryMemberProfile> GetMemberAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<DiscoveryProject>> GetCandidatesAsync(Guid userId, DateTime now, DateTime snapshot,
        DiscoveryFilters filters, CancellationToken ct = default);
}
