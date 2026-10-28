using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockProfileExtractionProviderTests
{
    private const string Lists = "{\"skill_ids\":{},\"experiences\":[],\"certificates\":[],\"warnings\":[]}";

    [Theory]
    [InlineData("disabled")]
    [InlineData("empty-key")]
    [InlineData("newline-key")]
    [InlineData("host-injection")]
    [InlineData("wrong-model")]
    [InlineData("bad-timeout")]
    public async Task InvalidConfigurationDoesNotSend(string variant)
    {
        var options = ValidOptions();
        switch (variant)
        {
            case "disabled": options.Enabled = false; break;
            case "empty-key": options.ApiKey = ""; break;
            case "newline-key": options.ApiKey = "key\r\nInjected: header"; break;
            case "host-injection": options.Region = "attacker.example/path"; break;
            case "wrong-model": options.ModelId = "other.model"; break;
            case "bad-timeout": options.TimeoutSeconds = 0; break;
        }
        using var handler = new StubHandler((_, _) => Task.FromResult(Success()));
        using var provider = Create(handler, options);
        var error = await Assert.ThrowsAsync<AiException>(() => Extract(provider));
        Assert.Equal(AiFailure.Unavailable, error.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(401, false, AiFailure.Unavailable)]
    [InlineData(403, false, AiFailure.Unavailable)]
    [InlineData(404, false, AiFailure.Unavailable)]
    [InlineData(429, false, AiFailure.Busy)]
    [InlineData(413, false, AiFailure.PayloadTooLarge)]
    [InlineData(408, false, AiFailure.Timeout)]
    [InlineData(504, false, AiFailure.Timeout)]
    [InlineData(400, true, AiFailure.DocumentRejected)]
    [InlineData(400, false, AiFailure.Provider)]
    [InlineData(500, false, AiFailure.Provider)]
    [InlineData(307, false, AiFailure.Provider)]
    public async Task ProviderErrorsAreSafeAndNeverRetried(int status, bool pdf, AiFailure expected)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent("sensitive source and test-api-key") }));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.ExtractListsAsync(new("source", pdf ? [1] : null), [], []));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("test-api-key", error.ToString());
        Assert.DoesNotContain("sensitive source", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"user\",\"status\":\"completed\",\"content\":[]}]}")]
    public async Task RejectsMalformedEnvelope(string response)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(response) }));
        using var provider = Create(handler);
        Assert.Equal(AiFailure.InvalidOutput, (await Assert.ThrowsAsync<AiException>(() => Extract(provider))).Failure);
    }

    [Fact]
    public async Task BoundsDecodedUtf8Output()
    {
        var oversized = "{\"value\":\"" + new string('é', AiProfileLimits.MaxOutputBytes / 2) + "\"}";
        using var handler = new StubHandler((_, _) => Task.FromResult(Success(oversized)));
        using var provider = Create(handler);
        Assert.Equal(AiFailure.InvalidOutput, (await Assert.ThrowsAsync<AiException>(() => Extract(provider))).Failure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundsEnvelopeWithOrWithoutContentLength(bool knownLength)
    {
        var oversized = new byte[1024 * 1024 + 1];
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = knownLength ? new ByteArrayContent(oversized) : new StreamContent(new NonSeekableStream(oversized)) }));
        using var provider = Create(handler);
        Assert.Equal(AiFailure.InvalidOutput, (await Assert.ThrowsAsync<AiException>(() => Extract(provider))).Failure);
    }

    [Fact]
    public async Task RejectsThirdConcurrentCallAndReleasesSlotsAfterCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new StubHandler(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) <= 2)
            {
                if (calls == 2) entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return Success();
        });
        using var provider = Create(handler);
        using var cancellation = new CancellationTokenSource();
        var first = Extract(provider, cancellation.Token);
        var second = Extract(provider, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AiFailure.Busy, (await Assert.ThrowsAsync<AiException>(() => Extract(provider))).Failure);
        Assert.Equal(2, handler.Calls);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(Lists, (await Extract(provider)).RawJson);
    }

    [Fact]
    public async Task TimeoutIsSafeAndSlotIsReleased()
    {
        var calls = 0;
        using var handler = new StubHandler(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, token);
            return Success();
        });
        var options = ValidOptions();
        options.TimeoutSeconds = 1;
        using var provider = Create(handler, options);
        Assert.Equal(AiFailure.Timeout, (await Assert.ThrowsAsync<AiException>(() => Extract(provider))).Failure);
        Assert.Equal(Lists, (await Extract(provider)).RawJson);
    }

    [Fact]
    public async Task NetworkExceptionDoesNotExposeInnerMessage()
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException("test-api-key private document"));
        using var provider = Create(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => Extract(provider));
        Assert.Equal(AiFailure.Provider, error.Failure);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("test-api-key", error.ToString());
    }

    private static Task<AiListModelOutput> Extract(BedrockProfileExtractionProvider provider, CancellationToken token = default) =>
        provider.ExtractListsAsync(new("source", null), [], [], token);

    private static BedrockOptions ValidOptions() => new() { Enabled = true, ApiKey = "test-api-key" };
    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler, BedrockOptions? options = null) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, Options.Create(options ?? ValidOptions()),
            NullLogger<BedrockProfileExtractionProvider>.Instance);

    private static HttpResponseMessage Success(string arguments = Lists) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[] { new { type = "function_call", status = "completed", name = "extract_lists", call_id = "call-1", arguments } },
            usage = new { input_tokens = 10, output_tokens = 20 }
        }), Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request, token); }
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    { public override bool CanSeek => false; }
}
