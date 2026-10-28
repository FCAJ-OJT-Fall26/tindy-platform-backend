using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const int MaxEnvelopeBytes = 1024 * 1024;
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly BedrockOptions _options = options.Value;

    public Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input,
        IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles, CancellationToken cancellationToken = default) =>
        SendAsync(input, () => CreateListRequest(input, skills, roles), ReadListOutput, cancellationToken);

    private async Task<AiListModelOutput> SendAsync(AiProfileInput input, Func<HttpRequestMessage> createRequest,
        Func<JsonElement, string> readOutput, CancellationToken cancellationToken, bool reserved = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConfiguration();
        if (!reserved) Reserve(1, cancellationToken);

        var timer = Stopwatch.StartNew();
        string? responseBody = null;
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
            responseBody = Encoding.UTF8.GetString(bytes);
            using var original = JsonDocument.Parse(bytes);
            using var document = IsGemma ? NormalizeGemmaResponse(original.RootElement) : JsonDocument.Parse(bytes);
            diagnostics = new AiCallDiagnostics(ReadStopReason(document.RootElement),
                ReadTokens(document.RootElement, "inputTokens"), ReadTokens(document.RootElement, "outputTokens"),
                OutputTokenLimit, timer.ElapsedMilliseconds);
            string result;
            try
            {
                result = readOutput(document.RootElement);
            }
            catch (AiException exception) when (exception.Failure == AiFailure.InvalidOutput)
            {
                var raw = ReadDiagnosticOutput(document.RootElement);
                throw new AiException(AiFailure.InvalidOutput, raw.Text, raw.Kind);
            }
            logger.LogInformation("AI extraction completed TraceId={TraceId} ElapsedMs={ElapsedMs} InputTokens={InputTokens} OutputTokens={OutputTokens}",
                Activity.Current?.TraceId.ToString(), timer.ElapsedMilliseconds,
                ReadTokens(document.RootElement, "inputTokens"), ReadTokens(document.RootElement, "outputTokens"));
            return new AiListModelOutput(result, diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AiException(AiFailure.Timeout) { Diagnostics = new(null, null, null, OutputTokenLimit, timer.ElapsedMilliseconds, "timeout") };
        }
        catch (AiException exception)
        {
            var reason = diagnostics?.StopReason == "max_tokens" ? "output_token_limit" :
                exception.Failure == AiFailure.InvalidOutput ? "invalid_tool_or_model_output" : "provider_http_error";
            exception.Diagnostics = (diagnostics ?? new(null, null, null, OutputTokenLimit, timer.ElapsedMilliseconds)) with { FailureReason = reason };
            logger.LogWarning("AI extraction failed TraceId={TraceId} Failure={Failure} StopReason={StopReason} ElapsedMs={ElapsedMs} OutputTokens={OutputTokens}",
                Activity.Current?.TraceId.ToString(), exception.Failure, exception.Diagnostics.StopReason,
                timer.ElapsedMilliseconds, exception.Diagnostics.OutputTokens);
            throw;
        }
        catch (JsonException)
        {
            throw new AiException(AiFailure.InvalidOutput, responseBody, "provider_response")
            { Diagnostics = new(null, null, null, OutputTokenLimit, timer.ElapsedMilliseconds, "invalid_provider_json") };
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

    private static string? ReadStopReason(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("stopReason", out var value) || value.ValueKind != JsonValueKind.String) return null;
        var reason = value.GetString();
        return reason is "end_turn" or "tool_use" or "max_tokens" or "stop_sequence" or
            "guardrail_intervened" or "content_filtered" or "malformed_model_output" or
            "malformed_tool_use" or "model_context_window_exceeded" ? reason : "unknown";
    }

    private HttpRequestMessage CreateListRequest(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles)
    {
        if (IsGemma) return CreateGemmaRequest(input, ListExtractionPrompt.System, ListExtractionPrompt.User(input, skills, roles),
            ListExtractionPrompt.ToolName, ListExtractionPrompt.Schema(skills, roles));
        var content = new List<object>();
        if (input.Pdf is not null)
            content.Add(new { document = new { format = "pdf", name = "CV", source = new { bytes = Convert.ToBase64String(input.Pdf) } } });
        content.Add(new { text = ListExtractionPrompt.User(input, skills, roles) });
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://bedrock-runtime.{_options.Region}.amazonaws.com/model/{Uri.EscapeDataString(_options.ModelId)}/converse");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            system = new[] { new { text = ListExtractionPrompt.System } },
            messages = new[] { new { role = "user", content } },
            inferenceConfig = new { maxTokens = 4096, temperature = 0.1 },
            additionalModelRequestFields = new { reasoningConfig = new { type = "disabled" } },
            toolConfig = new
            {
                tools = new[] { new { toolSpec = new { name = ListExtractionPrompt.ToolName,
                    description = "Extract only evidenced skills, experiences and certificates; unknown values are null.",
                    inputSchema = new { json = ListExtractionPrompt.Schema(skills, roles) } } } },
                toolChoice = new { tool = new { name = ListExtractionPrompt.ToolName } }
            }
        });
        return request;
    }

    private HttpRequestMessage CreateScalarRequest(AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
    {
        if (IsGemma) return CreateGemmaRequest(input, ScalarProfileExtractionPrompt.System, ScalarProfileExtractionPrompt.User(input, roles),
            ScalarProfileExtractionPrompt.ToolName, ScalarProfileExtractionPrompt.Schema(roles));
        var content = new List<object>();
        if (input.Pdf is not null)
            content.Add(new { document = new { format = "pdf", name = "CV", source = new { bytes = Convert.ToBase64String(input.Pdf) } } });
        content.Add(new { text = ScalarProfileExtractionPrompt.User(input, roles) });
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://bedrock-runtime.{_options.Region}.amazonaws.com/model/{Uri.EscapeDataString(_options.ModelId)}/converse");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            system = new[] { new { text = ScalarProfileExtractionPrompt.System } },
            messages = new[] { new { role = "user", content } },
            inferenceConfig = new { maxTokens = 4096, temperature = 0.1 },
            additionalModelRequestFields = new { reasoningConfig = new { type = "disabled" } },
            toolConfig = new
            {
                tools = new[] { new { toolSpec = new { name = ScalarProfileExtractionPrompt.ToolName,
                    description = "Extract only explicitly supported scalar profile facts, not lists.",
                    inputSchema = new { json = ScalarProfileExtractionPrompt.Schema(roles) } } } },
                toolChoice = new { tool = new { name = ScalarProfileExtractionPrompt.ToolName } }
            }
        });
        return request;
    }

    private string ReadListOutput(JsonElement root) => ReadToolOutput(root, ListExtractionPrompt.ToolName);

    private string ReadToolOutput(JsonElement root, string toolName)
    {
        if (ReadStopReason(root) != "tool_use" ||
            !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Object ||
            !output.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String || role.GetString() != "assistant" ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 1)
            throw new AiException(AiFailure.InvalidOutput);
        var block = content[0];
        if (block.ValueKind != JsonValueKind.Object || block.EnumerateObject().Count() != 1 ||
            !block.TryGetProperty("toolUse", out var tool) || tool.ValueKind != JsonValueKind.Object ||
            !tool.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != toolName ||
            !tool.TryGetProperty("toolUseId", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) ||
            !tool.TryGetProperty("input", out var input) ||
            (input.ValueKind != JsonValueKind.Object && !(IsGemma && input.ValueKind == JsonValueKind.String)))
            throw new AiException(AiFailure.InvalidOutput);
        var json = IsGemma && input.ValueKind == JsonValueKind.String ? input.GetString()! : input.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > AiProfileLimits.MaxOutputBytes) throw new AiException(AiFailure.InvalidOutput);
        if (IsGemma)
        {
            try
            {
                using var parsed = JsonDocument.Parse(json);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new AiException(AiFailure.InvalidOutput);
            }
            catch (JsonException) { throw new AiException(AiFailure.InvalidOutput); }
        }
        return json; // Preserve duplicate keys for the application's strict validators.
    }

    private void ValidateConfiguration()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)
            || _options.ApiKey.Any(character => character is <= ' ' or >= '\u007f')
            || _options.TimeoutSeconds is < 1 or > 120
            || (_options.ModelId != "global.amazon.nova-2-lite-v1:0" && !IsGemma)
            || (IsGemma && _options.Region is not ("us-east-1" or "us-east-2" or "us-west-2" or "eu-central-1"))
            || !Regex.IsMatch(_options.Region ?? string.Empty, @"\A[a-z]{2}-[a-z]+-\d\z", RegexOptions.CultureInvariant))
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
        if (content.Headers.ContentLength > MaxEnvelopeBytes)
            throw new AiException(AiFailure.InvalidOutput);
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > MaxEnvelopeBytes)
                throw new AiException(AiFailure.InvalidOutput);
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static (string Text, string Kind) ReadDiagnosticOutput(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object &&
            output.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            if (content.GetArrayLength() == 1 && content[0].ValueKind == JsonValueKind.Object &&
                content[0].TryGetProperty("toolUse", out var tool) && tool.ValueKind == JsonValueKind.Object &&
                tool.TryGetProperty("input", out var input))
                return (input.ValueKind == JsonValueKind.String ? input.GetString()! : input.GetRawText(), "tool_input");
            var parts = content.EnumerateArray()
                .Where(block => block.ValueKind == JsonValueKind.Object &&
                    block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(block => block.GetProperty("text").GetString()).ToArray();
            if (parts.Length > 0) return (string.Concat(parts), "model_text");
        }
        return (root.GetRawText(), "provider_response");
    }

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
