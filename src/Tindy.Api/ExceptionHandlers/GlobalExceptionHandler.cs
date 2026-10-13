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
            InvalidCredentialsException =>
                StatusCodes.Status401Unauthorized,

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
                401 => "Unauthorized",
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

        problemDetails.Extensions["error_code"] =
            exception switch
            {
                InvalidCredentialsException =>
                    "AUTH_INVALID_CREDENTIALS",

                EmailAlreadyExistsException =>
                    "AUTH_EMAIL_ALREADY_EXISTS",

                UsernameAlreadyExistsException =>
                    "USERNAME_ALREADY_EXISTS",

                OnboardingIncompleteException =>
                    "ONBOARDING_INCOMPLETE",

                ConflictException =>
                    "CONFLICT",

                NotFoundException =>
                    "NOT_FOUND",

                BusinessRuleException =>
                    "BUSINESS_RULE_VIOLATION",

                _ =>
                    "INTERNAL_SERVER_ERROR"
            };

        if (exception is OnboardingIncompleteException incomplete)
        {
            problemDetails.Extensions["missing_steps"] = incomplete.MissingSteps;
        }

        // 4. Include TraceId
        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        // 5. Set HTTP status and write response
        httpContext.Response.StatusCode = statusCode;

        httpContext.Response.ContentType =
            "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            cancellationToken);

        // 6. Exception was handled
        return true;
    }
}