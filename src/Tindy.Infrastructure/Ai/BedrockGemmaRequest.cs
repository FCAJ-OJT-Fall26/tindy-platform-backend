using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;

namespace Tindy.Infrastructure.Ai;

public sealed partial class BedrockProfileExtractionProvider
{
    private const int GemmaRequestLimit = 3_500_000;
    private const int OutputTokenLimit = 8192;

    private HttpRequestMessage CreateGemmaRequest(AiProfileInput input, string system, string user,
        string toolName, JsonObject schema)
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
        body["tools"] = new JsonArray(new JsonObject
        {
            ["type"] = "function", ["name"] = toolName,
            ["description"] = "Extract only explicitly supported facts using the provided schema and catalog keys.",
            // Dynamic skill/evidence maps use the existing schema; the server validates all values.
            ["parameters"] = GemmaSchema(schema), ["strict"] = false
        });
        body["tool_choice"] = new JsonObject { ["type"] = "function", ["name"] = toolName };
        body["parallel_tool_calls"] = false;
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

}
