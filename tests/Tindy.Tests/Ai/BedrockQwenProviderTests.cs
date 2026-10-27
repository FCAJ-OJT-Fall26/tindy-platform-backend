using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockQwenProviderTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jIikAAAAASUVORK5CYII=");

    [Fact]
    public async Task BatchRendersOnceAndSendsTheSameImagesToBothToolsWithoutNovaFields()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new Renderer(async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return [new("png", Png)]; });
        using var handler = new Stub(async (request, token) =>
        {
            Assert.Equal("bedrock-runtime.us-east-1.amazonaws.com", request.RequestUri!.Host);
            Assert.EndsWith("/model/qwen.qwen3-vl-235b-a22b/converse", Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement;
            Assert.False(root.TryGetProperty("additionalModelRequestFields", out _));
            Assert.True(root.GetProperty("toolConfig").GetProperty("toolChoice").TryGetProperty("any", out _));
            Assert.Equal(4096, root.GetProperty("inferenceConfig").GetProperty("maxTokens").GetInt32());
            var content = root.GetProperty("messages")[0].GetProperty("content");
            Assert.Equal("png", content[0].GetProperty("image").GetProperty("format").GetString());
            Assert.Equal(Convert.ToBase64String(Png), content[0].GetProperty("image").GetProperty("source").GetProperty("bytes").GetString());
            Assert.False(content[0].TryGetProperty("document", out _));
            using var source = JsonDocument.Parse(content[1].GetProperty("text").GetString()!);
            Assert.Equal("synthetic", source.RootElement.GetProperty("source_text").GetString());
            return Success(Tool(root));
        });
        using var provider = Create(handler, renderer);
        await using var batch = provider.BeginBatch();
        var input = new AiProfileInput("synthetic", "%PDF-test"u8.ToArray());
        var profile = batch.ExtractProfileScalarsAsync(input, []);
        var lists = batch.ExtractListsAsync(input, [], []);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, renderer.Calls);
        release.SetResult();
        await Task.WhenAll(profile, lists);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, renderer.Calls);
    }

    [Fact]
    public async Task TextOnlyNeedsNoRendererAndStillUsesTheStrictToolReader()
    {
        using var handler = new Stub((_, _) => Task.FromResult(Success("extract_lists")));
        var renderer = new Renderer((_, _) => throw new InvalidOperationException("Text-only must not render."));
        using var provider = Create(handler, renderer);
        var output = await provider.ExtractListsAsync(new("synthetic", null), [], []);
        Assert.Equal("tool_use", output.Diagnostics.StopReason);
        Assert.Equal(0, renderer.Calls);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("end_turn", "extract_lists")]
    [InlineData("max_tokens", "extract_lists")]
    [InlineData("tool_use", "wrong_tool")]
    public async Task IncompleteAndWrongToolOutputsAreNeverReturnedAsSuccessfulDrafts(string reason, string name)
    {
        using var handler = new Stub((_, _) => Task.FromResult(Success(name, reason)));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractListsAsync(new("synthetic", null), [], []));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.NotNull(error.RawOutput);
        Assert.Equal(reason, error.Diagnostics!.StopReason);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task UnsupportedRegionAndRenderFailureSendNoModelCallsAndReleaseCapacity()
    {
        using var handler = new Stub((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using (var wrongRegion = Create(handler, region: "ap-southeast-1"))
            Assert.Equal(AiFailure.Unavailable, Assert.Throws<AiException>(() => wrongRegion.BeginBatch()).Failure);
        var renderer = new Renderer((_, _) => throw new AiException(AiFailure.DocumentRejected));
        using var provider = Create(handler, renderer);
        await using (var batch = provider.BeginBatch())
        {
            var input = new AiProfileInput(null, "%PDF-bad"u8.ToArray());
            var profile = batch.ExtractProfileScalarsAsync(input, []);
            var lists = batch.ExtractListsAsync(input, [], []);
            await Assert.ThrowsAsync<AiException>(() => profile);
            await Assert.ThrowsAsync<AiException>(() => lists);
        }
        Assert.Equal(1, renderer.Calls);
        Assert.Equal(0, handler.Calls);
        await using var available = provider.BeginBatch();
    }

    [Fact]
    public async Task DisposingBatchCancelsSharedRenderingBeforeReleasingSlots()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var renderer = new Renderer(async (_, token) =>
        {
            Interlocked.Increment(ref active); entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return []; }
            finally { Interlocked.Decrement(ref active); }
        });
        using var handler = new Stub((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var provider = Create(handler, renderer);
        var batch = provider.BeginBatch();
        var input = new AiProfileInput(null, "%PDF-test"u8.ToArray());
        var profile = batch.ExtractProfileScalarsAsync(input, []);
        var lists = batch.ExtractListsAsync(input, [], []);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await batch.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => profile);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lists);
        Assert.Equal(0, active);
        Assert.Equal(1, renderer.Calls);
        Assert.Equal(0, handler.Calls);
        await using var available = provider.BeginBatch();
    }

    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler, IPdfPageRenderer? renderer = null, string region = "us-east-1") =>
        new(new HttpClient(handler), Options.Create(new BedrockOptions { Enabled = true, ApiKey = "synthetic-key", Region = region }),
            NullLogger<BedrockProfileExtractionProvider>.Instance, renderer);
    private static string Tool(JsonElement root) => root.GetProperty("toolConfig").GetProperty("tools")[0].GetProperty("toolSpec").GetProperty("name").GetString()!;
    private static HttpResponseMessage Success(string name, string reason = "tool_use") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            stopReason = reason, output = new { message = new { role = "assistant", content = new[] { new { toolUse = new { toolUseId = "synthetic-tool", name, input = new { synthetic = true } } } } } },
            usage = new { inputTokens = 100, outputTokens = 50 }
        }))
    };
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return callback(request, token); }
    }
    private sealed class Renderer(Func<byte[], CancellationToken, Task<IReadOnlyList<AiDocumentImage>>> callback) : IPdfPageRenderer
    {
        public int Calls;
        public Task<IReadOnlyList<AiDocumentImage>> RenderAsync(byte[] pdf, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Calls); return callback(pdf, cancellationToken); }
    }
}
