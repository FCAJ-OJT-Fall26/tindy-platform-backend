using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class AiListExtractionService(ICatalogRepository catalog, IAiListExtractionProvider provider) : IAiListExtractionService
{
    public async Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        input = AiExtractionInput.Normalize(input);

        var active = (await catalog.GetActiveSkillsAsync(null, null, cancellationToken))
            .Where(item => item.IsActive).Select(item => new AiCatalogItem(item.SkillId, item.Name));
        var references = new AiSkillReferenceMap(active);
        var output = await provider.ExtractListsAsync(input, references.References, cancellationToken);
        try
        {
            var draft = AiListDraftValidator.Parse(output.RawJson, references);
            return draft with { Diagnostics = output.Diagnostics };
        }
        catch (AiException exception) when (exception.Failure == AiFailure.InvalidOutput)
        {
            exception.Diagnostics = output.Diagnostics with { FailureReason = exception.ValidationReason ?? "invalid_list_schema" };
            throw;
        }
    }

}
