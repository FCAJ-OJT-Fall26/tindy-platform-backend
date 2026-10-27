using System.Text.Json.Serialization;

namespace Tindy.Application.DTOs.Ai;

public sealed record AiCallDiagnostics(
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("input_tokens")] int? InputTokens,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    [property: JsonPropertyName("max_output_tokens")] int MaxOutputTokens,
    [property: JsonPropertyName("elapsed_ms")] long ElapsedMs,
    [property: JsonPropertyName("failure_reason")] string? FailureReason = null,
    [property: JsonPropertyName("stage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stage = null,
    [property: JsonPropertyName("stages"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, AiCallDiagnostics>? Stages = null);

public sealed record AiListModelOutput(string RawJson, AiCallDiagnostics Diagnostics);

public sealed record AiScalarProfileModelOutput(string RawJson, AiCallDiagnostics Diagnostics);
