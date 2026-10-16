using Microsoft.AspNetCore.Authorization;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authorization;

namespace Tindy.Api.Authorization;

/// <summary>
/// Re-checks the current account state from the database on every
/// AuthenticatedOnly request. An unexpired JWT is not sufficient for
/// a suspended, soft-deleted or missing account.
/// </summary>
public sealed class ActiveAccountHandler : AuthorizationHandler<ActiveAccountRequirement>
{
    private readonly IOnboardingAccessReader _reader;

    public ActiveAccountHandler(IOnboardingAccessReader reader) => _reader = reader;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveAccountRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId) ||
            userId == Guid.Empty)
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted
            ?? CancellationToken.None;
        var status = await _reader.GetStatusAsync(userId, cancellationToken);

        if (status is OnboardingAccessStatus.Completed or OnboardingAccessStatus.Incomplete)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}
