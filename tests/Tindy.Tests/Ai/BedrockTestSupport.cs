using System.Net;
using System.Text.Json;
using Tindy.Application.DTOs.Ai;
using Tindy.Infrastructure.Ai;

namespace Tindy.Tests.Ai;

internal static class BedrockTestSupport
{
    public static HttpResponseMessage ToolResponse(string name, string arguments, string stop = "tool_use", int count = 1)
    {
        var output = new List<object>();
        if (stop == "end_turn")
            output.Add(new { type = "message", role = "assistant", status = "completed",
                content = new[] { new { type = "output_text", text = "No tool result." } } });
        else
            output.AddRange(Enumerable.Range(0, count).Select(index => (object)new
            { type = "function_call", status = "completed", call_id = "call-" + index, name, arguments }));
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            status = stop == "max_tokens" ? "incomplete" : "completed", output,
            incomplete_details = new { reason = "max_output_tokens" }, usage = new { input_tokens = 100, output_tokens = 50 }
        })) };
    }

    // Exercise scalar extraction through the same reservation/session used in production.
    public static async Task<AiScalarProfileModelOutput> ExtractProfileForTestAsync(
        this BedrockProfileExtractionProvider provider, AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
    {
        await using var batch = provider.BeginBatch();
        return await batch.ExtractProfileScalarsAsync(input, roles);
    }
}
