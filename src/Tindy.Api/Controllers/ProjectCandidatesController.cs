using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{project_id:guid}/positions/{position_id:guid}/candidates")]
public sealed class ProjectCandidatesController(TeammateDiscoveryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute(Name = "project_id")] Guid projectId, [FromRoute(Name = "position_id")] Guid positionId,
        [FromQuery] int limit = 20, [FromQuery] string? cursor = null, [FromQuery(Name = "sort_by")] string sortBy = "match",
        [FromQuery(Name = "role_id")] Guid? roleId = null, [FromQuery(Name = "skill_id")] Guid? skillId = null,
        [FromQuery(Name = "min_available_hours_per_week")] int? hours = null, CancellationToken ct = default)
    {
        if (Request.Query.TryGetValue("cursor", out var suppliedCursor) && string.IsNullOrWhiteSpace(suppliedCursor.ToString()))
            throw new ProjectException(400, "INVALID_TEAMMATE_DISCOVERY_CURSOR", "Invalid candidate cursor.");
        RecruitmentQuery.RequireOnly(Request, "limit", "cursor", "sort_by", "role_id", "skill_id", "min_available_hours_per_week");
        var data = await service.GetAsync(Guid.Parse(User.FindFirst("sub")!.Value), projectId, positionId, limit, cursor,
            new(roleId, skillId, hours, sortBy), ct);
        return Ok(new { success = true, message = "Project candidates retrieved.", data });
    }
}
