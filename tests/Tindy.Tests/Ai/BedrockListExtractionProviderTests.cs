using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockListExtractionProviderTests
{
    private const string Lists = "{\"skill_ids\":{\"s1\":\"Python\"},\"experiences\":[],\"certificates\":[],\"warnings\":[]}";

    [Fact]
    public async Task UsesOneForcedToolWithSkillAndRoleReferencesAndPdfBeforeText()
    {
        using var handler = new Stub(async (request, token) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement;
            Assert.Equal("extract_lists", root.GetProperty("tool_choice").GetProperty("name").GetString());
            Assert.Single(root.GetProperty("tools").EnumerateArray());
            var schema = root.GetProperty("tools")[0].GetProperty("parameters");
            Assert.Equal(4, schema.GetProperty("required").GetArrayLength());
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            var keys = schema.GetProperty("properties").GetProperty("skill_ids");
            Assert.False(keys.TryGetProperty("propertyNames", out _));
            Assert.False(keys.TryGetProperty("maxProperties", out _));
            Assert.Equal("string", keys.GetProperty("additionalProperties").GetProperty("type").GetString());
            var experience = schema.GetProperty("properties").GetProperty("experiences").GetProperty("items").GetProperty("properties");
            Assert.False(experience.TryGetProperty("role_title", out _));
            Assert.True(experience.TryGetProperty("role_title_source", out _));
            Assert.Equal(new string?[] { null, "r1" }, experience.GetProperty("role_key").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(8192, root.GetProperty("max_output_tokens").GetInt32());
            var content = root.GetProperty("input")[1].GetProperty("content");
            Assert.Equal("input_file", content[0].GetProperty("type").GetString());
            using var source = JsonDocument.Parse(content[1].GetProperty("text").GetString()!);
            Assert.Equal("s1", source.RootElement.GetProperty("active_skills")[0].GetProperty("key").GetString());
            Assert.Equal("Python", source.RootElement.GetProperty("active_skills")[0].GetProperty("name").GetString());
            Assert.Equal("r1", source.RootElement.GetProperty("active_roles")[0].GetProperty("key").GetString());
            Assert.Equal("AI Engineer", source.RootElement.GetProperty("active_roles")[0].GetProperty("name").GetString());
            return Success();
        });
        using var provider = Create(handler);
        var result = await provider.ExtractListsAsync(new("I used Python", "%PDF-fixture"u8.ToArray()), [new("s1", "Python")], [new("r1", "AI Engineer")]);
        Assert.Equal(Lists, result.RawJson);
        Assert.Equal("tool_use", result.Diagnostics.StopReason);
        Assert.Equal(100, result.Diagnostics.InputTokens);
        Assert.Equal(50, result.Diagnostics.OutputTokens);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task EmptyCatalogAllowsOnlyAnEmptyMapWithoutAnInvalidEmptyEnum()
    {
        using var handler = new Stub(async (request, token) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var schema = json.RootElement.GetProperty("tools")[0].GetProperty("parameters").GetProperty("properties").GetProperty("skill_ids");
            Assert.False(schema.TryGetProperty("maxProperties", out _));
            Assert.False(schema.TryGetProperty("propertyNames", out _));
            var experience = json.RootElement.GetProperty("tools")[0].GetProperty("parameters").GetProperty("properties").GetProperty("experiences").GetProperty("items").GetProperty("properties");
            Assert.Equal(JsonValueKind.Null, Assert.Single(experience.GetProperty("role_key").GetProperty("enum").EnumerateArray()).ValueKind);
            return Success();
        });
        using var provider = Create(handler);
        await provider.ExtractListsAsync(new("source", null), [], []);
    }

    [Theory]
    [InlineData("max_tokens", "extract_lists", 1)]
    [InlineData("end_turn", "extract_lists", 1)]
    [InlineData("tool_use", "unknown", 1)]
    [InlineData("tool_use", "extract_lists", 2)]
    public async Task RejectsIncompleteWrongOrMultipleToolsAndRetainsDiagnostics(string stop, string name, int count)
    {
        using var handler = new Stub((_, _) => Task.FromResult(Success(stop, name, count)));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractListsAsync(new("source", null), [], []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.NotNull(error.RawOutput);
        Assert.Equal(stop, error.Diagnostics!.StopReason);
        Assert.Equal(stop == "max_tokens" ? "output_token_limit" : "invalid_tool_or_model_output", error.Diagnostics.FailureReason);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"status\":\"completed\",\"output\":{}}") ]
    public async Task RejectsMalformedProviderEnvelopes(string body)
    {
        using var handler = new Stub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var provider = Create(handler);
        Assert.Equal(AiFailure.InvalidOutput, (await Assert.ThrowsAsync<AiException>(() => provider.ExtractListsAsync(new("source", null), [], []))).Failure);
    }

    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, Options.Create(new BedrockOptions { Enabled = true, ApiKey = "test-key" }),
            NullLogger<BedrockProfileExtractionProvider>.Instance);

    private static HttpResponseMessage Success(string stop = "tool_use", string name = "extract_lists", int count = 1) =>
        BedrockTestSupport.ToolResponse(name, Lists, stop, count);

    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return callback(request, token);
        }
    }
}
