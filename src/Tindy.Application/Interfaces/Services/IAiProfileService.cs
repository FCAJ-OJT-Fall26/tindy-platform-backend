using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Interfaces.Services;

public interface IAiProfileService
{
    Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default);
}
