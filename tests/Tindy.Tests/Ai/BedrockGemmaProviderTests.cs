using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockGemmaProviderTests
{
    [Fact]
    public async Task SendsNativePdfWithForcedToolAndReasoning_WithoutStoringTheResponse()
    {
        var pdf = "%PDF-synthetic"u8.ToArray();
        using var handler = new Stub(async request =>
        {
            Assert.Equal("https://bedrock-mantle.us-east-1.api.aws/openai/v1/responses", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var root = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.Equal("google.gemma-4-31b", root["model"]!.GetValue<string>());
            Assert.False(root["store"]!.GetValue<bool>());
            Assert.Equal("high", root["reasoning"]!["effort"]!.GetValue<string>());
            Assert.Equal(8192, root["max_output_tokens"]!.GetValue<int>());
            Assert.Equal("system", root["input"]![0]!["role"]!.GetValue<string>());
            var content = root["input"]![1]!["content"]!;
            Assert.Equal("data:application/pdf;base64," + Convert.ToBase64String(pdf), content[0]!["file_data"]!.GetValue<string>());
            Assert.Equal("extract_profile_scalars", root["tool_choice"]!["name"]!.GetValue<string>());
            Assert.False(root["parallel_tool_calls"]!.GetValue<bool>());
            Assert.Contains("r1", root["tools"]![0]!["parameters"]!.ToJsonString());
            return Response();
        });
        using var provider = Create(handler);
        var result = await provider.ExtractProfileForTestAsync(new("Vietnamese source", pdf), [new("r1", "AI Engineer")]);
        Assert.Equal("{\"profile\":{\"display_name\":\"Lan\"},\"warnings\":[]}", result.RawJson);
        Assert.Equal("tool_use", result.Diagnostics.StopReason);
        Assert.Equal(100, result.Diagnostics.InputTokens);
        Assert.Equal(50, result.Diagnostics.OutputTokens);
        Assert.Equal(8192, result.Diagnostics.MaxOutputTokens);
    }

    [Fact]
    public async Task ListStageUsesExistingCatalogSchemaAndAcceptsReasoningBeforeFunction()
    {
        using var handler = new Stub(async request =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.Equal("extract_lists", body["tool_choice"]!["name"]!.GetValue<string>());
            Assert.Contains("s1", body["input"]!.ToJsonString());
            Assert.DoesNotContain("propertyNames", body["tools"]![0]!["parameters"]!.ToJsonString());
            Assert.DoesNotContain("maxProperties", body["tools"]![0]!["parameters"]!.ToJsonString());
            Assert.DoesNotContain("input_file", body["input"]!.ToJsonString());
            return Response("extract_lists", "{\"profile\":{\"skill_ids\":{\"s1\":\"Python\"}},\"experiences\":[],\"certificates\":[],\"warnings\":[]}");
        });
        using var provider = Create(handler);
        var output = await provider.ExtractListsAsync(new("Python", null), [new("s1", "Python")], []);
        Assert.Contains("Python", output.RawJson);
        Assert.DoesNotContain("private reasoning", output.RawJson);
    }

    [Theory]
    [InlineData("wrong_tool", "{}", "completed", false)]
    [InlineData("extract_profile_scalars", "{broken", "completed", false)]
    [InlineData("extract_profile_scalars", "[]", "completed", false)]
    [InlineData("extract_profile_scalars", "{}", "incomplete", false)]
    [InlineData("extract_profile_scalars", "{}", "completed", true)]
    public async Task RejectsWrongMalformedTruncatedOrDuplicateFunctionResults(string name, string arguments, string status, bool duplicate)
    {
        using var handler = new Stub(_ => Task.FromResult(Response(name, arguments, status, duplicate)));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileForTestAsync(new("source", null), []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.DoesNotContain("private reasoning", error.RawOutput ?? "");
        if (status == "incomplete")
        {
            Assert.Equal("max_tokens", error.Diagnostics!.StopReason);
            Assert.Equal("output_token_limit", error.Diagnostics.FailureReason);
        }
    }

    [Fact]
    public async Task MalformedProviderJsonDoesNotDiscloseReasoning()
    {
        using var handler = new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"output\":[{\"type\":\"reasoning\",\"text\":\"private reasoning\"},") }));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileForTestAsync(new("source", null), []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.Equal("invalid_provider_json", error.Diagnostics!.FailureReason);
        Assert.Null(error.RawOutput);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("pending-call")]
    [InlineData("empty-call-id")]
    [InlineData("extra-output")]
    public async Task RejectsUnfinishedCallsOrExtraOutputWithoutDisclosingReasoning(string variant)
    {
        using var handler = new Stub(async _ =>
        {
            using var response = Response();
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            if (variant is "failed" or "cancelled") node["status"] = variant;
            if (variant == "pending-call") node["output"]![1]!["status"] = "in_progress";
            if (variant == "empty-call-id") node["output"]![1]!["call_id"] = " ";
            if (variant == "extra-output") node["output"]!.AsArray().Add(new JsonObject
            { ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray() });
            return new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString()) };
        });
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileForTestAsync(new("source", null), []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.DoesNotContain("private reasoning", error.RawOutput ?? "");
    }

    [Fact]
    public async Task PreservesDuplicateJsonPropertiesForStrictApplicationValidation()
    {
        const string arguments = "{\"profile\":{},\"profile\":{\"display_name\":\"Lan\"},\"warnings\":[]}";
        using var handler = new Stub(_ => Task.FromResult(Response(arguments: arguments)));
        using var provider = Create(handler);
        var result = await provider.ExtractProfileForTestAsync(new("source", null), []);
        Assert.Equal(arguments, result.RawJson);
        var error = Assert.Throws<AiException>(() => AiScalarProfileDraftValidator.Parse(result.RawJson, new AiRoleReferenceMap([])));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
    }

    [Fact]
    public async Task RejectsOversizedSerializedPdfBeforeNetwork_AndReleasesCapacity()
    {
        using var handler = new Stub(_ => Task.FromResult(Response()));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileForTestAsync(new(null, new byte[2_700_000]), []));
        Assert.Equal(AiFailure.PayloadTooLarge, error.Failure);
        Assert.Equal(0, handler.Calls);
        await provider.ExtractProfileForTestAsync(new("small source", null), []);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task UnsupportedGemmaRegionDoesNotSend()
    {
        using var handler = new Stub(_ => Task.FromResult(Response()));
        using var provider = Create(handler, "ap-southeast-1");
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractProfileForTestAsync(new("source", null), []));
        Assert.Equal(AiFailure.Unavailable, error.Failure);
        Assert.Equal(0, handler.Calls);
    }

    private static HttpResponseMessage Response(string name = "extract_profile_scalars",
        string arguments = "{\"profile\":{\"display_name\":\"Lan\"},\"warnings\":[]}", string status = "completed", bool duplicate = false)
    {
        var call = new { type = "function_call", status = "completed", name, call_id = "call-1", arguments };
        var output = new List<object> { new { type = "reasoning", summary = new[] { new { text = "private reasoning" } } }, call };
        if (duplicate) output.Add(call);
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            status, output, usage = new { input_tokens = 100, output_tokens = 50 },
            incomplete_details = new { reason = "max_output_tokens" }
        })) };
    }

    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler, string region = "us-east-1") =>
        new(new HttpClient(handler), Options.Create(new BedrockOptions
        { Enabled = true, ApiKey = "test-key", ModelId = "google.gemma-4-31b", Region = region }),
            NullLogger<BedrockProfileExtractionProvider>.Instance);

    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return callback(request); }
    }
}
