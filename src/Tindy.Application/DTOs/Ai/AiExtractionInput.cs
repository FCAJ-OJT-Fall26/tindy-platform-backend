using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

public static class AiExtractionInput
{
    public static AiProfileInput Normalize(AiProfileInput input)
    {
        if (input.Text?.Length > AiProfileLimits.MaxTextLength)
            throw Invalid("text", "Text must not exceed 20000 characters.");
        var text = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
        if (input.Pdf?.Length > AiProfileLimits.MaxPdfBytes) throw new AiException(AiFailure.PayloadTooLarge);
        if (input.Pdf is not null && !input.Pdf.AsSpan().StartsWith("%PDF-"u8))
            throw new AiException(AiFailure.DocumentRejected);
        if (text is null && input.Pdf is null) throw Invalid("input", "Provide a PDF file and/or non-empty text.");
        return new(text, input.Pdf);
    }

    private static ProfileValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
