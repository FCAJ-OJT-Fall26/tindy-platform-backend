using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Interfaces.Services;

public interface IAiListExtractionService
{
    Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default);
}
