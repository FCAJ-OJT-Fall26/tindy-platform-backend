using Tindy.Application.DTOs.Ai;
using Tindy.Infrastructure.Ai;

namespace Tindy.Tests.Ai;

internal static class BedrockTestSupport
{
    // Exercise scalar extraction through the same reservation/session used in production.
    public static async Task<AiScalarProfileModelOutput> ExtractProfileForTestAsync(
        this BedrockProfileExtractionProvider provider, AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
    {
        await using var batch = provider.BeginBatch();
        return await batch.ExtractProfileScalarsAsync(input, roles);
    }
}
