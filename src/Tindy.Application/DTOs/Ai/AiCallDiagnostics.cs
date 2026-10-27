using System.Text.Json.Serialization;

namespace Tindy.Application.DTOs.Ai;

public sealed record AiCallDiagnostics(
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("input_tokens")] int? InputTokens,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    [property: JsonPropertyName("max_output_tokens")] int MaxOutputTokens,
    [property: JsonPropertyName("elapsed_ms")] long ElapsedMs,
    [property: JsonPropertyName("failure_reason")] string? FailureReason = null);

public sealed record AiListModelOutput(string RawJson, AiCallDiagnostics Diagnostics);
