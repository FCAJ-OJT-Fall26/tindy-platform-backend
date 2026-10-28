using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class BedrockExtractionBatchTests
{
    [Fact]
    public async Task BatchReservesBothSlotsAndRejectsOtherWorkBeforeHttp()
    {
        using var handler = new BlockingHandler();
        using var provider = Create(handler);
        await using (var batch = provider.BeginBatch())
        {
            Assert.Equal(AiFailure.Busy, Assert.Throws<AiException>(() => provider.BeginBatch()).Failure);
            Assert.Equal(AiFailure.Busy, (await Assert.ThrowsAsync<AiException>(() => provider.ExtractListsAsync(new("x", null), [], []))).Failure);
            Assert.Equal(0, handler.Calls);
            var profile = batch.ExtractProfileScalarsAsync(new("x", null), []);
            var lists = batch.ExtractListsAsync(new("x", null), [], []);
            await handler.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Throws<InvalidOperationException>(() => { _ = batch.ExtractListsAsync(new("x", null), [], []); });
            await batch.DisposeAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => profile);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lists);
            Assert.Equal(0, handler.Active);
        }
        await using var next = provider.BeginBatch(); // Double disposal did not over-release.
    }

    [Fact]
    public async Task OccupiedSingleSlotPreventsHalfStartingBatchAndLeavesOtherSlotUsable()
    {
        using var handler = new BlockingHandler();
        using var provider = Create(handler);
        using var cancellation = new CancellationTokenSource();
        var single = provider.ExtractListsAsync(new("x", null), [], [], cancellation.Token);
        Assert.Equal(AiFailure.Busy, Assert.Throws<AiException>(() => provider.BeginBatch()).Failure);
        Assert.Equal(1, handler.Calls);
        var second = provider.ExtractListsAsync(new("x", null), [], [], cancellation.Token);
        await handler.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => single);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await using var next = provider.BeginBatch();
    }

    [Fact]
    public async Task CancelledReservationAndDisposedBatchCannotStartWork()
    {
        using var handler = new BlockingHandler();
        using var provider = Create(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => provider.BeginBatch(cancellation.Token));
        var batch = provider.BeginBatch();
        await batch.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => { _ = batch.ExtractProfileScalarsAsync(new("x", null), []); });
        Assert.Equal(0, handler.Calls);
        await using var next = provider.BeginBatch();
    }

    private static BedrockProfileExtractionProvider Create(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new BedrockOptions { Enabled = true, ApiKey = "test-key", ModelId = "global.amazon.nova-2-lite-v1:0", Region = "ap-southeast-1" }), NullLogger<BedrockProfileExtractionProvider>.Instance);

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public int Calls;
        public int Active;
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Active);
            if (Interlocked.Increment(ref Calls) == 2) BothStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}
