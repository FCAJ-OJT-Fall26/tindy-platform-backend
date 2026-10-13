using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Tindy.Api.Authorization;

public sealed class OnboardingAuthorizationResultHandler
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext httpContext,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var onboardingRequired = authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailureReasons.Any(
                reason => reason.Message == OnboardingPolicies.OnboardingRequired) == true;

        if (!onboardingRequired)
        {
            // Preserve normal JWT 401 challenges and generic 403 forbids.
            return _defaultHandler.HandleAsync(
                next, httpContext, policy, authorizeResult);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = "Bạn cần hoàn tất 8 bước onboarding để truy cập chức năng này.",
            Instance = httpContext.Request.Path.ToString()
        };

        problem.Extensions["error_code"] = OnboardingPolicies.OnboardingRequired;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        httpContext.Response.ContentType = "application/problem+json";

        return httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(problem),
            httpContext.RequestAborted);
    }
}
