using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

// DefaultPolicy requires JWT, active caller and completed onboarding.
// Unlike MeController, do not permit AuthenticatedOnly/pre-onboarding access.
[ApiController]
[Authorize]
[Route("api/users/{user_id:guid}/profile")]
public sealed class MemberProfileController : ControllerBase
{
    private readonly IProfileService _profileService;

    public MemberProfileController(IProfileService profileService) => _profileService = profileService;

    [HttpGet]
    public async Task<IActionResult> GetMemberProfile([FromRoute(Name = "user_id")] Guid userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var callerId) || callerId == Guid.Empty)
            throw new InvalidCredentialsException();

        var data = await _profileService.GetMemberProfileAsync(userId, cancellationToken);
        return Ok(new { success = true, message = "Member profile retrieved successfully.", data });
    }
}
