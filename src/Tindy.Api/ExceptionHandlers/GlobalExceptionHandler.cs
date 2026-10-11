using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;

namespace Tindy.Api.ExceptionHandlers;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // 1. Map Exception -> HTTP Status Code
        var statusCode = exception switch
        {
            NotFoundException =>
                StatusCodes.Status404NotFound,

            ConflictException =>
                StatusCodes.Status409Conflict,

            BusinessRuleException =>
                StatusCodes.Status422UnprocessableEntity,

            _ =>
                StatusCodes.Status500InternalServerError
        };

        // 2. Logging
        if (statusCode >= 500)
        {
            _logger.LogError(
                exception,
                "Unhandled exception occurred. TraceId: {TraceId}",
                httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(
                "Request failed. StatusCode: {StatusCode}, " +
                "Exception: {ExceptionType}, TraceId: {TraceId}",
                statusCode,
                exception.GetType().Name,
                httpContext.TraceIdentifier);
        }

        // 3. Create ProblemDetails
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,

            Title = statusCode switch
            {
                404 => "Not Found",
                409 => "Conflict",
                422 => "Unprocessable Entity",
                _ => "Internal Server Error"
            },

            Detail = statusCode == 500
                ? "An unexpected error occurred."
                : exception.Message,

            Instance = httpContext.Request.Path
        };

        // 4. Include TraceId
        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        // 5. Set HTTP status and write response
        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            cancellationToken);

        // 6. Exception was handled
        return true;
    }
}