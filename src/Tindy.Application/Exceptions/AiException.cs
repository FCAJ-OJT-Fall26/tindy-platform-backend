using System.Text;
using Tindy.Application.DTOs.Ai;

namespace Tindy.Application.Exceptions;

public enum AiFailure
{
    Unavailable,
    Busy,
    Provider,
    InvalidOutput,
    Timeout,
    DocumentRejected,
    PayloadTooLarge,
    UnsupportedMedia
}

// Messages remain public and fixed. Bounded invalid-output diagnostics are exposed only in Development.
public sealed class AiException(AiFailure failure) : Exception(failure switch
{
    AiFailure.Unavailable => "AI extraction is unavailable. Check the server configuration or try again later.",
    AiFailure.Busy => "AI extraction is busy. Try again later.",
    AiFailure.InvalidOutput => "AI returned an incomplete or invalid profile draft. Please try again.",
    AiFailure.Timeout => "AI extraction timed out. Please try again.",
    AiFailure.DocumentRejected => "The PDF could not be processed. Use an unencrypted, supported PDF.",
    AiFailure.PayloadTooLarge => "The uploaded request exceeds the permitted size.",
    AiFailure.UnsupportedMedia => "Use multipart/form-data with a PDF file and/or text.",
    _ => "The AI provider could not process the request. Please try again later."
})
{
    public AiException(AiFailure failure, string? rawOutput, string rawOutputKind = "model_text") : this(failure)
    {
        if (failure != AiFailure.InvalidOutput || rawOutput is null) return;
        var buffer = new byte[AiProfileLimits.MaxOutputBytes];
        Encoding.UTF8.GetEncoder().Convert(rawOutput.AsSpan(), buffer, true,
            out var charactersUsed, out _, out var completed);
        RawOutput = rawOutput[..charactersUsed];
        RawOutputTruncated = !completed;
        RawOutputKind = rawOutputKind;
    }

    // Kept out of Message/InnerException/ToString so normal exception logging cannot print the draft.
    public string? RawOutput { get; }
    public bool RawOutputTruncated { get; }
    public string? RawOutputKind { get; }
    public AiFailure Failure { get; } = failure;
    public string ErrorCode => Failure switch
    {
        AiFailure.Unavailable => "AI_UNAVAILABLE",
        AiFailure.Busy => "AI_BUSY",
        AiFailure.InvalidOutput => "AI_INVALID_OUTPUT",
        AiFailure.Timeout => "AI_TIMEOUT",
        AiFailure.DocumentRejected => "AI_DOCUMENT_REJECTED",
        AiFailure.PayloadTooLarge => "AI_PAYLOAD_TOO_LARGE",
        AiFailure.UnsupportedMedia => "AI_UNSUPPORTED_MEDIA",
        _ => "AI_PROVIDER_ERROR"
    };
}
