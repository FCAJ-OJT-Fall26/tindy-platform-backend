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
public sealed class BedrockProfileExtractionProvider(
    HttpClient httpClient,
    IOptions<BedrockOptions> options,
    ILogger<BedrockProfileExtractionProvider> logger) : IProfileExtractionProvider, IDisposable
{
    private const int MaxEnvelopeBytes = 1024 * 1024;
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly BedrockOptions _options = options.Value;

    public async Task<string> ExtractAsync(AiProfileInput input, IReadOnlyList<AiCatalogItem> skills,
        IReadOnlyList<AiCatalogItem> roles, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConfiguration();
        if (!await _slots.WaitAsync(0, cancellationToken))
            throw new AiException(AiFailure.Busy);

        var timer = Stopwatch.StartNew();
        string? responseBody = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var request = CreateRequest(input, skills, roles);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new AiException(MapFailure(response.StatusCode, input.Pdf is not null));

            var bytes = await ReadBoundedAsync(response.Content, timeout.Token);
            responseBody = Encoding.UTF8.GetString(bytes);
            using var document = JsonDocument.Parse(bytes);
            string result;
            try
            {
                result = ReadOutput(document.RootElement);
            }
            catch (AiException exception) when (exception.Failure == AiFailure.InvalidOutput)
            {
                var raw = ReadDiagnosticOutput(document.RootElement);
                throw new AiException(AiFailure.InvalidOutput, raw.Text, raw.Kind);
            }
            logger.LogInformation("AI extraction completed TraceId={TraceId} ElapsedMs={ElapsedMs} InputTokens={InputTokens} OutputTokens={OutputTokens}",
                Activity.Current?.TraceId.ToString(), timer.ElapsedMilliseconds,
                ReadTokens(document.RootElement, "inputTokens"), ReadTokens(document.RootElement, "outputTokens"));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AiException(AiFailure.Timeout);
        }
        catch (JsonException)
        {
            throw new AiException(AiFailure.InvalidOutput, responseBody, "provider_response");
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
            _slots.Release();
        }
    }

    private void ValidateConfiguration()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)
            || _options.ApiKey.Any(character => character is <= ' ' or >= '\u007f')
            || _options.TimeoutSeconds is < 1 or > 120
            || _options.ModelId != "global.amazon.nova-2-lite-v1:0"
            || !Regex.IsMatch(_options.Region ?? string.Empty, @"\A[a-z]{2}-[a-z]+-\d\z", RegexOptions.CultureInvariant))
            throw new AiException(AiFailure.Unavailable);
    }

    private HttpRequestMessage CreateRequest(AiProfileInput input, IReadOnlyList<AiCatalogItem> skills,
        IReadOnlyList<AiCatalogItem> roles)
    {
        var content = new List<object> { new { text = ProfileExtractionPrompt.User(input, skills, roles) } };
        if (input.Pdf is not null)
            content.Add(new { document = new { format = "pdf", name = "CV", source = new { bytes = Convert.ToBase64String(input.Pdf) } } });

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://bedrock-runtime.{_options.Region}.amazonaws.com/model/{Uri.EscapeDataString(_options.ModelId)}/converse");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            system = new[] { new { text = ProfileExtractionPrompt.System } },
            messages = new[] { new { role = "user", content } },
            inferenceConfig = new { maxTokens = 4096, temperature = 0.1 },
            additionalModelRequestFields = new { reasoningConfig = new { type = "disabled" } }
        });
        return request;
    }

    private static AiFailure MapFailure(HttpStatusCode status, bool hasPdf) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => AiFailure.Unavailable,
        HttpStatusCode.TooManyRequests => AiFailure.Busy,
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

    private static string ReadOutput(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("stopReason", out var reason) || reason.ValueKind != JsonValueKind.String || reason.GetString() != "end_turn"
            || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Object
            || !output.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String || role.GetString() != "assistant"
            || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            throw new AiException(AiFailure.InvalidOutput);

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || block.EnumerateObject().Count() != 1
                || !block.TryGetProperty("text", out var part) || part.ValueKind != JsonValueKind.String)
                throw new AiException(AiFailure.InvalidOutput);
            text.Append(part.GetString());
        }
        var result = text.ToString();
        if (string.IsNullOrWhiteSpace(result) || Encoding.UTF8.GetByteCount(result) > AiProfileLimits.MaxOutputBytes)
            throw new AiException(AiFailure.InvalidOutput);
        return result;
    }

    private static (string Text, string Kind) ReadDiagnosticOutput(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object &&
            output.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var parts = content.EnumerateArray()
                .Where(block => block.ValueKind == JsonValueKind.Object &&
                    block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(block => block.GetProperty("text").GetString()).ToArray();
            if (parts.Length > 0) return (string.Concat(parts), "model_text");
        }
        return (root.GetRawText(), "provider_response");
    }

    private static int? ReadTokens(JsonElement root, string name) =>
        root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
        && usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var count) && count >= 0 ? count : null;

    public void Dispose()
    {
        _slots.Dispose();
        httpClient.Dispose();
    }
}
