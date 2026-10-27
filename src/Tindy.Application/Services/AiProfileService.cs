using Tindy.Application.DTOs.Ai;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

// Phase one extracts lists only; the public endpoint and draft shape stay compatible.
// A later phase can compose scalar-profile extraction here without changing the list service.
public sealed class AiProfileService(IAiListExtractionService lists) : IAiProfileService
{
    public Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default) =>
        lists.ExtractAsync(input, cancellationToken);
}
