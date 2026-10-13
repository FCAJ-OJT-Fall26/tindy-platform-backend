using Microsoft.AspNetCore.Authorization;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authorization;

namespace Tindy.Api.Authorization;

public sealed class OnboardingCompletedHandler
    : AuthorizationHandler<OnboardingCompletedRequirement>
{
    private readonly IOnboardingAccessReader _reader;

    public OnboardingCompletedHandler(IOnboardingAccessReader reader)
    {
        _reader = reader;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OnboardingCompletedRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId) ||
            userId == Guid.Empty)
        {
            return;
        }

        // HTTP request cancellation should stop the database query.
        var ct = (context.Resource as HttpContext)?.RequestAborted
            ?? CancellationToken.None;

        var status = await _reader.GetStatusAsync(userId, ct);

        switch (status)
        {
            case OnboardingAccessStatus.Completed:
                context.Succeed(requirement);
                break;

            case OnboardingAccessStatus.Incomplete:
                context.Fail(new AuthorizationFailureReason(this, OnboardingPolicies.OnboardingRequired));
                break;

            // Inactive/deleted/missing accounts must not pass the gate.
            // They are not incorrectly reported as incomplete onboarding.
            case OnboardingAccessStatus.InactiveOrMissing:
                context.Fail();
                break;
        }
    }
}
