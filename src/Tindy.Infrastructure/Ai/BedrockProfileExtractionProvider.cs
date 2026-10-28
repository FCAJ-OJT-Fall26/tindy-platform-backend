using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Infrastructure.Ai;

// Singleton registration makes the limit process-wide. No queue, retry, or repair invocation.
public sealed partial class BedrockProfileExtractionProvider(
    HttpClient httpClient,
    IOptions<BedrockOptions> options,
    ILogger<BedrockProfileExtractionProvider> logger) : IAiListExtractionProvider, IAiExtractionBatchProvider, IDisposable
{
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly BedrockOptions _options = options.Value;

    public Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input,
        IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles, CancellationToken cancellationToken = default) =>
        SendAsync(input, () => CreateListRequest(input, skills, roles), ListExtractionPrompt.ToolName, cancellationToken);

    private async Task<AiListModelOutput> SendAsync(AiProfileInput input, Func<HttpRequestMessage> createRequest,
        string toolName, CancellationToken cancellationToken, bool reserved = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConfiguration();
        if (!reserved) Reserve(1, cancellationToken);

        var timer = Stopwatch.StartNew();
        AiCallDiagnostics? diagnostics = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var request = createRequest();
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new AiException(MapFailure(response.StatusCode, input.Pdf is not null));

            var bytes = await ReadBoundedAsync(response.Content, timeout.Token);
            using var document = JsonDocument.Parse(bytes);
            diagnostics = new AiCallDiagnostics(ReadStopReason(document.RootElement),
                ReadTokens(document.RootElement, "input_tokens"), ReadTokens(document.RootElement, "output_tokens"),
                AiProfileLimits.MaxOutputTokensPerStage, timer.ElapsedMilliseconds);
            string result;
            try
            {
                result = ReadToolOutput(document.RootElement, toolName);
            }
            catch (AiException exception) when (exception.Failure == AiFailure.InvalidOutput)
            {
                var raw = ReadDiagnosticOutput(document.RootElement);
                throw new AiException(AiFailure.InvalidOutput, raw.Text, raw.Kind);
            }
            logger.LogInformation("AI extraction completed TraceId={TraceId} ElapsedMs={ElapsedMs} InputTokens={InputTokens} OutputTokens={OutputTokens}",
                Activity.Current?.TraceId.ToString(), timer.ElapsedMilliseconds,
                ReadTokens(document.RootElement, "input_tokens"), ReadTokens(document.RootElement, "output_tokens"));
            return new AiListModelOutput(result, diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AiException(AiFailure.Timeout) { Diagnostics = new(null, null, null, AiProfileLimits.MaxOutputTokensPerStage, timer.ElapsedMilliseconds, "timeout") };
        }
        catch (AiException exception)
        {
            var reason = diagnostics?.StopReason == "max_tokens" ? "output_token_limit" :
                exception.Failure == AiFailure.InvalidOutput ? "invalid_tool_or_model_output" : "provider_http_error";
            exception.Diagnostics = (diagnostics ?? new(null, null, null, AiProfileLimits.MaxOutputTokensPerStage, timer.ElapsedMilliseconds)) with { FailureReason = reason };
            logger.LogWarning("AI extraction failed TraceId={TraceId} Failure={Failure} StopReason={StopReason} ElapsedMs={ElapsedMs} OutputTokens={OutputTokens}",
                Activity.Current?.TraceId.ToString(), exception.Failure, exception.Diagnostics.StopReason,
                timer.ElapsedMilliseconds, exception.Diagnostics.OutputTokens);
            throw;
        }
        catch (JsonException)
        {
            throw new AiException(AiFailure.InvalidOutput)
            { Diagnostics = new(null, null, null, AiProfileLimits.MaxOutputTokensPerStage, timer.ElapsedMilliseconds, "invalid_provider_json") };
        }
        catch (HttpRequestException)
        {
            throw new AiException(AiFailure.Provider);
        }
        catch (IOException)
        {
            throw new AiException(AiFailure.Provider);
        }
        finally
        {
            if (!reserved) Release(1);
        }
    }

    private static string? ReadStopReason(JsonElement root) => String(root, "status") switch
    {
        "completed" => OutputItems(root).Any(item => String(item, "type") == "function_call") ? "tool_use" : "end_turn",
        "incomplete" when root.TryGetProperty("incomplete_details", out var details)
            && String(details, "reason") == "max_output_tokens" => "max_tokens",
        null => null,
        _ => "unknown"
    };

    private HttpRequestMessage CreateListRequest(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles) =>
        CreateGemmaRequest(input, ListExtractionPrompt.System, ListExtractionPrompt.User(input, skills, roles),
            ListExtractionPrompt.ToolName, ListExtractionPrompt.Schema(skills, roles));

    private HttpRequestMessage CreateScalarRequest(AiProfileInput input, IReadOnlyList<AiRoleReference> roles) =>
        CreateGemmaRequest(input, ScalarProfileExtractionPrompt.System, ScalarProfileExtractionPrompt.User(input, roles),
            ScalarProfileExtractionPrompt.ToolName, ScalarProfileExtractionPrompt.Schema(roles));

    private static string ReadToolOutput(JsonElement root, string toolName)
    {
        var items = OutputItems(root).Where(item => String(item, "type") != "reasoning").ToArray();
        if (String(root, "status") != "completed" || items.Length != 1)
            throw new AiException(AiFailure.InvalidOutput);
        var call = items[0];
        if (String(call, "type") != "function_call" || String(call, "status") != "completed"
            || String(call, "name") != toolName || string.IsNullOrWhiteSpace(String(call, "call_id"))
            || String(call, "arguments") is not { } json || Encoding.UTF8.GetByteCount(json) > AiProfileLimits.MaxOutputBytes)
            throw new AiException(AiFailure.InvalidOutput);
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new AiException(AiFailure.InvalidOutput);
        }
        catch (JsonException) { throw new AiException(AiFailure.InvalidOutput); }
        return json; // Preserve duplicate keys for the application's strict validators.
    }

    private void ValidateConfiguration()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)
            || _options.ApiKey.Any(character => character is <= ' ' or >= '\u007f')
            || _options.TimeoutSeconds is < 1 or > 120
            || _options.ModelId != "google.gemma-4-31b"
            || _options.Region is not ("us-east-1" or "us-east-2" or "us-west-2" or "eu-central-1"))
            throw new AiException(AiFailure.Unavailable);
    }

    private static AiFailure MapFailure(HttpStatusCode status, bool hasPdf) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => AiFailure.Unavailable,
        HttpStatusCode.TooManyRequests => AiFailure.Busy,
        HttpStatusCode.RequestEntityTooLarge => AiFailure.PayloadTooLarge,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AiFailure.Timeout,
        HttpStatusCode.BadRequest when hasPdf => AiFailure.DocumentRejected,
        _ => AiFailure.Provider
    };

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > AiProfileLimits.MaxProviderResponseBytes)
            throw new AiException(AiFailure.InvalidOutput);
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > AiProfileLimits.MaxProviderResponseBytes)
                throw new AiException(AiFailure.InvalidOutput);
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static (string Text, string Kind) ReadDiagnosticOutput(JsonElement root)
    {
        var calls = OutputItems(root).Where(item => String(item, "type") == "function_call").ToArray();
        if (calls.Length == 1 && String(calls[0], "arguments") is { } arguments)
            return (arguments, "tool_input");
        var text = OutputItems(root).Where(item => String(item, "type") == "message")
            .Where(item => item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(part => String(part, "type") == "output_text")
            .Select(part => String(part, "text")).ToArray();
        if (text.Length > 0) return (string.Concat(text), "model_text");
        // Keep only function diagnostics; never copy reasoning blocks or the full provider envelope.
        return (JsonSerializer.Serialize(new { status = String(root, "status"), calls }), "provider_response");
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static IEnumerable<JsonElement> OutputItems(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array
            ? output.EnumerateArray() : [];

    private static int? ReadTokens(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
        && usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var count) && count >= 0 ? count : null;

    public void Dispose()
    {
        _slots.Dispose();
        httpClient.Dispose();
    }
}
