using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Interfaces.Services;

public interface IProfileExtractionProvider
{
    Task<string> ExtractAsync(AiProfileInput input, IReadOnlyList<AiCatalogItem> skills,
        IReadOnlyList<AiCatalogItem> roles, CancellationToken cancellationToken = default);
}
