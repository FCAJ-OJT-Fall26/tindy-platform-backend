using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Interfaces.Services;

// A lease reserves both calls before either can start. Disposal cancels and drains
// in-flight work before returning capacity to the process-wide provider.
public interface IAiExtractionBatchProvider
{
    IAiExtractionBatchSession BeginBatch(CancellationToken cancellationToken = default);
}

public interface IAiExtractionBatchSession : IAsyncDisposable
{
    Task<AiScalarProfileModelOutput> ExtractProfileScalarsAsync(AiProfileInput input, IReadOnlyList<AiRoleReference> roles);
    Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input, IReadOnlyList<AiSkillReference> skills);
}
