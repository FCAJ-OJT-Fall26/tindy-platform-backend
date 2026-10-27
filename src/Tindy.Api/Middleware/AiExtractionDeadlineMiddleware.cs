using Tindy.Application.Exceptions;

namespace Tindy.Api.Middleware;

public sealed class AiExtractionDeadlineMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) => InvokeWithBudgetAsync(context, next, TimeSpan.FromSeconds(120));

    // Public for deterministic short-budget tests; production always uses 120s.
    public static async Task InvokeWithBudgetAsync(HttpContext context, RequestDelegate next, TimeSpan budget)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !context.Request.Path.Equals("/api/ai/extract-profile", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        var original = context.RequestAborted;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(original);
        deadline.CancelAfter(budget);
        context.RequestAborted = deadline.Token;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (!original.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            // Restore before the outer exception handler writes the 504 response.
            context.RequestAborted = original;
            throw new AiException(AiFailure.Timeout)
            { Diagnostics = new(null, null, null, 8192, (long)budget.TotalMilliseconds, "request_deadline", "request") };
        }
        finally { context.RequestAborted = original; }
    }
}
