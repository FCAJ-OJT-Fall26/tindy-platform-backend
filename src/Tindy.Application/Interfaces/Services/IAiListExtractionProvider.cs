using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Interfaces.Services;

public interface IAiListExtractionProvider
{
    Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input,
        IReadOnlyList<AiSkillReference> skills, CancellationToken cancellationToken = default);
}
