using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;

namespace Tindy.Infrastructure.Ai;

public sealed partial class BedrockProfileExtractionProvider
{
    private const int GemmaRequestLimit = 3_500_000;
    private bool IsGemma => _options.ModelId == "google.gemma-4-31b";
    private int OutputTokenLimit => IsGemma ? 8192 : 4096;

    private HttpRequestMessage CreateGemmaRequest(AiProfileInput input, string system, string user,
        string? toolName = null, JsonObject? schema = null)
    {
        var content = new JsonArray();
        if (input.Pdf is not null)
            content.Add(new JsonObject
            {
                ["type"] = "input_file", ["filename"] = "CV.pdf",
                ["file_data"] = "data:application/pdf;base64," + Convert.ToBase64String(input.Pdf)
            });
        content.Add(new JsonObject { ["type"] = "input_text", ["text"] = user });
        var body = new JsonObject
        {
            ["model"] = _options.ModelId, ["store"] = false,
            ["input"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = content }),
            ["max_output_tokens"] = OutputTokenLimit,
            ["reasoning"] = new JsonObject { ["effort"] = "high" }
        };
        if (toolName is not null)
        {
            body["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "function", ["name"] = toolName,
                ["description"] = "Extract only explicitly supported facts using the provided schema and catalog keys.",
                // Dynamic skill/evidence maps use the existing schema; the server validates all values.
                ["parameters"] = GemmaSchema(schema!), ["strict"] = false
            });
            body["tool_choice"] = new JsonObject { ["type"] = "function", ["name"] = toolName };
            body["parallel_tool_calls"] = false;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        if (bytes.Length > GemmaRequestLimit) throw new AiException(AiFailure.PayloadTooLarge);
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://bedrock-mantle.{_options.Region}.api.aws/openai/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return request;
    }

    private static JsonNode GemmaSchema(JsonNode schema)
    {
        var result = schema.DeepClone();
        Adapt(result);
        return result;

        static void Adapt(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                // Mantle rejects these object-map keywords. Catalog keys, evidence and list limits
                // remain enforced by AiListDraftValidator; the short-key catalog stays in the prompt.
                if (obj.Remove("propertyNames", out _) || obj.ContainsKey("maxProperties"))
                    obj["description"] = "Map only supplied active skill keys to verbatim evidence; obey the server's skill count limits.";
                obj.Remove("maxProperties");
                foreach (var field in obj) Adapt(field.Value);
            }
            else if (node is JsonArray array)
                foreach (var item in array) Adapt(item);
        }
    }

    // Normalize transport metadata only. The existing readers still enforce one complete tool result,
    // and the application validators resolve references and validate the unchanged public draft.
    private static JsonDocument NormalizeGemmaResponse(JsonElement root)
    {
        var content = new JsonArray();
        var status = String(root, "status");
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
            throw new AiException(AiFailure.InvalidOutput);
        foreach (var item in output.EnumerateArray())
        {
            switch (String(item, "type"))
            {
                case "reasoning": break; // Never include reasoning text in the draft or raw-output panel.
                case "function_call" when String(item, "status") == "completed":
                    content.Add(new JsonObject { ["toolUse"] = new JsonObject
                    {
                        ["toolUseId"] = String(item, "call_id"), ["name"] = String(item, "name"), ["input"] = String(item, "arguments")
                    } });
                    break;
                case "message" when String(item, "role") == "assistant" && String(item, "status") == "completed":
                    if (!item.TryGetProperty("content", out var parts) || parts.ValueKind != JsonValueKind.Array)
                    { content.Add(new JsonObject { ["invalid"] = true }); break; }
                    foreach (var part in parts.EnumerateArray())
                        content.Add(String(part, "type") == "output_text" && String(part, "text") is { } text
                            ? new JsonObject { ["text"] = text } : new JsonObject { ["invalid"] = true });
                    break;
                default: content.Add(new JsonObject { ["invalid"] = true }); break;
            }
        }
        var reason = status == "completed"
            ? content.Any(block => block is JsonObject obj && obj.ContainsKey("toolUse")) ? "tool_use" : "end_turn"
            : status == "incomplete" && root.TryGetProperty("incomplete_details", out var details)
                && String(details, "reason") == "max_output_tokens" ? "max_tokens" : "unknown";
        var normalized = new JsonObject
        {
            ["stopReason"] = reason,
            ["output"] = new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } },
            ["usage"] = new JsonObject { ["inputTokens"] = ReadTokens(root, "input_tokens"), ["outputTokens"] = ReadTokens(root, "output_tokens") }
        };
        return JsonDocument.Parse(normalized.ToJsonString());
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
