using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Interfaces.Services;

public interface IAiScalarProfileProvider
{
    Task<AiScalarProfileModelOutput> ExtractProfileScalarsAsync(AiProfileInput input,
        IReadOnlyList<AiRoleReference> roles, CancellationToken cancellationToken = default);
}
