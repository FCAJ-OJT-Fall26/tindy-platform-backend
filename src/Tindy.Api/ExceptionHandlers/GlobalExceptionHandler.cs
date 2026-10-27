using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;

namespace Tindy.Api.ExceptionHandlers;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment? _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment? environment = null)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // 1. Map Exception -> HTTP Status Code
        var statusCode = exception switch
        {
            AiException ai => ai.Failure switch
            {
                AiFailure.Unavailable => StatusCodes.Status503ServiceUnavailable,
                AiFailure.Busy => StatusCodes.Status429TooManyRequests,
                AiFailure.Timeout => StatusCodes.Status504GatewayTimeout,
                AiFailure.DocumentRejected => StatusCodes.Status422UnprocessableEntity,
                AiFailure.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
                AiFailure.UnsupportedMedia => StatusCodes.Status415UnsupportedMediaType,
                _ => StatusCodes.Status502BadGateway
            },

            InvalidCredentialsException =>
                StatusCodes.Status401Unauthorized,

            ProfileValidationException =>
                StatusCodes.Status400BadRequest,

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
            // Provider/HTTP/database exceptions can include credentials or token payloads.
            // Keep an operational identifier without serializing exception text or inner exceptions.
            _logger.LogError("Unhandled exception occurred. Exception: {ExceptionType}, TraceId: {TraceId}",
                exception.GetType().Name, httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning("Request failed. StatusCode: {StatusCode}, " + "Exception: {ExceptionType}, TraceId: {TraceId}", statusCode, exception.GetType().Name, httpContext.TraceIdentifier);
        }

        // 3. Create ProblemDetails
        ProblemDetails problemDetails = exception is ProfileValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(entry => entry.Key, entry => entry.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "The input data is invalid.",
                Instance = httpContext.Request.Path
            }
            : new ProblemDetails
            {
                Status = statusCode,

                Title = statusCode switch
                {
                    401 => "Unauthorized",
                    404 => "Not Found",
                    409 => "Conflict",
                    413 => "Payload Too Large",
                    415 => "Unsupported Media Type",
                    422 => "Unprocessable Entity",
                    429 => "Too Many Requests",
                    502 => "Bad Gateway",
                    503 => "Service Unavailable",
                    504 => "Gateway Timeout",
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
                AiException ai => ai.ErrorCode,
                ProfileValidationException =>
                    "VALIDATION_FAILED",

                InvalidCredentialsException =>
                    "AUTH_INVALID_CREDENTIALS",

                EmailAlreadyExistsException =>
                    "AUTH_EMAIL_ALREADY_EXISTS",

                UsernameAlreadyExistsException =>
                    "USERNAME_ALREADY_EXISTS",

                OnboardingAlreadyCompletedException =>
                    "ONBOARDING_ALREADY_COMPLETED",

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

        if (_environment?.IsDevelopment() == true &&
            exception is AiException { Failure: AiFailure.InvalidOutput, RawOutput: not null } diagnostic)
        {
            problemDetails.Extensions["raw_model_output"] = diagnostic.RawOutput;
            problemDetails.Extensions["raw_output_kind"] = diagnostic.RawOutputKind;
            problemDetails.Extensions["raw_output_truncated"] = diagnostic.RawOutputTruncated;
            httpContext.Response.Headers.CacheControl = "no-store";
        }

        if (_environment?.IsDevelopment() == true && exception is AiException aiDiagnostic)
        {
            if (aiDiagnostic.Diagnostics is not null) problemDetails.Extensions["ai_diagnostics"] = aiDiagnostic.Diagnostics;
            if (aiDiagnostic.ValidationReason is not null) problemDetails.Extensions["validation_reason"] = aiDiagnostic.ValidationReason;
            httpContext.Response.Headers.CacheControl = "no-store";
        }

        // 5. Set HTTP status and write response
        httpContext.Response.StatusCode = statusCode;

        httpContext.Response.ContentType =
            "application/problem+json";

        // Serialize validation responses as their concrete type so the "errors" member is included.
        if (problemDetails is ValidationProblemDetails validationProblemDetails)
        {
            await httpContext.Response.WriteAsJsonAsync(validationProblemDetails,
                options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        }
        else
        {
            await httpContext.Response.WriteAsJsonAsync(problemDetails,
                options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        }

        // 6. Exception was handled
        return true;
    }
}
