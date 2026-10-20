using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

// Use the DefaultPolicy: JWT Bearer + active account + completed onboarding.
// Do NOT use MeController's pre-onboarding AuthenticatedOnly policy.
[ApiController]
[Authorize]
[Route("api/me/profile/experiences")]
public sealed class MyExperiencesController : ControllerBase
{
    private readonly IExperienceService _experienceService;

    public MyExperiencesController(IExperienceService experienceService) => _experienceService = experienceService;

    private Guid CurrentUserId
    {
        get
        {
            if (Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) && userId != Guid.Empty)
                return userId;
            throw new InvalidCredentialsException();
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var data = await _experienceService.ListMyExperiencesAsync(CurrentUserId, cancellationToken);
        return Ok(new { success = true, message = "Experiences retrieved successfully.", data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement json, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        var request = ExperienceRequestParser.Parse(json);
        var data = await _experienceService.CreateAsync(userId, request, cancellationToken);
        // There is intentionally no GET-by-id endpoint in the STEP 01 HTTP contract.
        // Return 201 with the created DTO rather than a Location pointing to a missing route.
        return StatusCode(StatusCodes.Status201Created,
            new { success = true, message = "Experience created successfully.", data });
    }

    [HttpPut("{experience_id:guid}")]
    public async Task<IActionResult> Update([FromRoute(Name = "experience_id")] Guid experienceId,
        [FromBody] JsonElement json, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        var request = ExperienceRequestParser.Parse(json);
        var data = await _experienceService.UpdateAsync(userId, experienceId, request, cancellationToken);
        return Ok(new { success = true, message = "Experience updated successfully.", data });
    }

    [HttpDelete("{experience_id:guid}")]
    public async Task<IActionResult> Delete([FromRoute(Name = "experience_id")] Guid experienceId,
        CancellationToken cancellationToken)
    {
        await _experienceService.DeleteAsync(CurrentUserId, experienceId, cancellationToken);
        return NoContent();
    }
}
