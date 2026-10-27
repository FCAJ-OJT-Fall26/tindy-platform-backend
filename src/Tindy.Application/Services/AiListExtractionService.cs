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
        if (input.Text?.Length > AiProfileLimits.MaxTextLength)
            throw InvalidInput("text", "Text must not exceed 20000 characters.");
        var text = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
        if (input.Pdf?.Length > AiProfileLimits.MaxPdfBytes)
            throw new AiException(AiFailure.PayloadTooLarge);
        if (input.Pdf is not null && (input.Pdf.Length < 5 || !input.Pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8)))
            throw new AiException(AiFailure.DocumentRejected);
        if (text is null && input.Pdf is null)
            throw InvalidInput("input", "Provide a PDF file and/or non-empty text.");

        var active = (await catalog.GetActiveSkillsAsync(null, null, cancellationToken))
            .Where(item => item.IsActive).Select(item => new AiCatalogItem(item.SkillId, item.Name));
        var references = new AiSkillReferenceMap(active);
        var output = await provider.ExtractListsAsync(new(text, input.Pdf), references.References, cancellationToken);
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

    private static ProfileValidationException InvalidInput(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
