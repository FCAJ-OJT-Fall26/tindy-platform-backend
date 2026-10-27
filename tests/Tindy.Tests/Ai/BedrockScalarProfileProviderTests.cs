using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockScalarProfileProviderTests
{
    [Fact]
    public async Task SendsOnlyRoleReferencesAndScalarToolWithPdfFirst()
    {
        using var handler = new Stub(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = json.RootElement;
            var content = root.GetProperty("messages")[0].GetProperty("content");
            Assert.True(content[0].TryGetProperty("document", out _));
            using var source = JsonDocument.Parse(content[1].GetProperty("text").GetString()!);
            Assert.Equal("r1", source.RootElement.GetProperty("active_roles")[0].GetProperty("key").GetString());
            Assert.False(source.RootElement.TryGetProperty("active_skills", out _));
            var tools = root.GetProperty("toolConfig");
            Assert.Equal("extract_profile_scalars", tools.GetProperty("toolChoice").GetProperty("tool").GetProperty("name").GetString());
            var schema = tools.GetProperty("tools")[0].GetProperty("toolSpec").GetProperty("inputSchema").GetProperty("json");
            var properties = schema.GetProperty("properties");
            Assert.Equal(2, properties.EnumerateObject().Count());
            var fields = properties.GetProperty("profile").GetProperty("properties");
            Assert.False(fields.TryGetProperty("skill_ids", out _));
            Assert.False(fields.TryGetProperty("bio", out _));
            Assert.False(fields.TryGetProperty("preferred_role_id", out _));
            Assert.Equal(4096, root.GetProperty("inferenceConfig").GetProperty("maxTokens").GetInt32());
            return Success();
        });
        using var provider = Create(handler);
        var result = await provider.ExtractProfileScalarsAsync(new("My name is Lan", "%PDF-test"u8.ToArray()), [new("r1", "AI Engineer")]);
        Assert.Contains("Lan", result.RawJson);
        Assert.Equal("tool_use", result.Diagnostics.StopReason);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("end_turn", "extract_profile_scalars")]
    [InlineData("max_tokens", "extract_profile_scalars")]
    [InlineData("tool_use", "extract_lists")]
    public async Task RejectsWrongOrIncompleteToolResults(string reason, string name)
    {
        using var handler = new Stub(_ => Task.FromResult(Success(reason, name)));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileScalarsAsync(new("source", null), []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.Equal(reason, error.Diagnostics!.StopReason);
        Assert.NotNull(error.RawOutput);
    }

    internal static HttpResponseMessage Success(string reason = "tool_use", string name = "extract_profile_scalars") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            stopReason = reason,
            output = new { message = new { role = "assistant", content = new[] { new { toolUse = new { toolUseId = "profile-1", name,
                input = new { profile = new { display_name = "Lan" }, warnings = Array.Empty<object>() } } } } } },
            usage = new { inputTokens = 100, outputTokens = 40 }
        }))
    };
    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new BedrockOptions { Enabled = true, ApiKey = "test-key" }), NullLogger<BedrockProfileExtractionProvider>.Instance);
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return callback(request); }
    }
}
