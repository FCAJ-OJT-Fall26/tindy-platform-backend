using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

// Read-only member route; DefaultPolicy enforces caller JWT, active account
// and completed onboarding. Target eligibility is enforced in Application.
[ApiController]
[Authorize]
[Route("api/users/{user_id:guid}/profile/experiences")]
public sealed class MemberExperiencesController : ControllerBase
{
    private readonly IExperienceService _experienceService;

    public MemberExperiencesController(IExperienceService experienceService) => _experienceService = experienceService;

    [HttpGet]
    public async Task<IActionResult> List([FromRoute(Name = "user_id")] Guid userId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var callerUserId) || callerUserId == Guid.Empty)
            throw new InvalidCredentialsException();

        var data = await _experienceService.ListMemberExperiencesAsync(userId, cancellationToken);
        return Ok(new { success = true, message = "Member experiences retrieved successfully.", data });
    }
}
