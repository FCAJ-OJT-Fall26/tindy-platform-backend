using Microsoft.AspNetCore.Http;
using Tindy.Api.Middleware;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiExtractionDeadlineTests
{
    [Fact]
    public async Task DeadlineCancelsBodyOrCatalogWorkAndRestoresTokenForErrorResponse()
    {
        var context = Context();
        var original = context.RequestAborted;
        var error = await Assert.ThrowsAsync<AiException>(() => AiExtractionDeadlineMiddleware.InvokeWithBudgetAsync(context,
            async ctx => await Task.Delay(Timeout.Infinite, ctx.RequestAborted), TimeSpan.FromMilliseconds(30)));
        Assert.Equal(AiFailure.Timeout, error.Failure);
        Assert.Equal("request", error.Diagnostics!.Stage);
        Assert.Equal(16384, error.Diagnostics.MaxOutputTokens);
        Assert.Equal(original, context.RequestAborted);
        Assert.False(context.RequestAborted.IsCancellationRequested);
    }

    [Fact]
    public async Task ClientAbortStaysCancellation()
    {
        var context = Context();
        using var client = new CancellationTokenSource();
        context.RequestAborted = client.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AiExtractionDeadlineMiddleware.InvokeWithBudgetAsync(context,
            async ctx => { client.Cancel(); await Task.Delay(Timeout.Infinite, ctx.RequestAborted); }, TimeSpan.FromSeconds(1)));
        Assert.Equal(client.Token, context.RequestAborted);
    }

    [Fact]
    public async Task OtherRoutesAreNotGivenAiDeadline()
    {
        var context = Context();
        context.Request.Path = "/api/me";
        var original = context.RequestAborted;
        await AiExtractionDeadlineMiddleware.InvokeWithBudgetAsync(context,
            ctx => { Assert.Equal(original, ctx.RequestAborted); return Task.CompletedTask; }, TimeSpan.Zero);
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/ai/extract-profile";
        return context;
    }
}
