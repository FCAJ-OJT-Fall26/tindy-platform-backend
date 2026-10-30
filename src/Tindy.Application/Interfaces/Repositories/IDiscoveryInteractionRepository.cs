using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Interfaces.Repositories;

public interface IDiscoveryInteractionRepository
{
    Task SetAsync(Guid userId, Guid projectId, bool savedFlag, bool value, DateTime now, CancellationToken ct = default);
    Task<SavedDiscoveryPage> GetSavedAsync(Guid userId, int page, int pageSize, DateTime now, CancellationToken ct = default);
}
