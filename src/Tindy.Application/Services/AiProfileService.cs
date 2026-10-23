using System.Text;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class AiProfileService(ICatalogRepository catalog, IProfileExtractionProvider provider) : IAiProfileService
{
    public async Task<ProfileDraftResponse> ExtractAsync(AiProfileInput input, CancellationToken cancellationToken = default)
    {
        var text = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
        if (input.Text?.Length > AiProfileLimits.MaxTextLength)
            throw InvalidInput("text", "Text must not exceed 20000 characters.");
        if (input.Pdf?.Length > AiProfileLimits.MaxPdfBytes)
            throw new AiException(AiFailure.PayloadTooLarge);
        if (input.Pdf is not null && (input.Pdf.Length < 5 || !input.Pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8)))
            throw new AiException(AiFailure.DocumentRejected);
        if (text is null && input.Pdf is null)
            throw InvalidInput("input", "Provide a PDF file and/or non-empty text.");

        // These reads share a scoped EF context and must remain sequential.
        var skills = (await catalog.GetActiveSkillsAsync(null, null, cancellationToken))
            .Where(x => x.IsActive).Select(x => new AiCatalogItem(x.SkillId, x.Name)).ToArray();
        var roles = (await catalog.GetActiveProfessionalRolesAsync(null, cancellationToken))
            .Where(x => x.IsActive).Select(x => new AiCatalogItem(x.ProfessionalRoleId, x.Name)).ToArray();
        var output = await provider.ExtractAsync(new AiProfileInput(text, input.Pdf), skills, roles, cancellationToken);
        if (output is null || Encoding.UTF8.GetByteCount(output) > AiProfileLimits.MaxOutputBytes)
            throw new AiException(AiFailure.InvalidOutput);
        return ProfileDraftValidator.Parse(output, skills.Select(x => x.Id).ToHashSet(), roles.Select(x => x.Id).ToHashSet());
    }

    private static ProfileValidationException InvalidInput(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
