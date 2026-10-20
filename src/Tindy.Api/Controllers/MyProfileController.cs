using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

// Uses the DefaultPolicy (JWT + active + completed onboarding).
// Do not inherit the AuthenticatedOnly policy from MeController.
[ApiController]
[Authorize]
[Route("api/me/profile")]
public sealed class MyProfileController : ControllerBase
{
    private readonly IProfileService _profileService;

    public MyProfileController(IProfileService profileService) => _profileService = profileService;

    [HttpGet]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
            throw new InvalidCredentialsException();

        var data = await _profileService.GetMyProfileAsync(userId, cancellationToken);
        return Ok(new { success = true, message = "User profile retrieved successfully.", data });
    }
}
