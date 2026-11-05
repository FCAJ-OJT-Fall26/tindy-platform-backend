using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{project_id:guid}/invitations")]
public sealed class ProjectInvitationsController(ProjectInvitationService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpPost]
    [EnableRateLimiting("project-invitations")]
    public async Task<IActionResult> Send([FromRoute(Name = "project_id")] Guid projectId, [FromBody] JsonElement json, CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request);
        var data = await service.SendAsync(UserId, projectId, CreateProjectInvitationRequest.Parse(json), ct);
        return StatusCode(201, new { success = true, message = "Project invitation sent.", data });
    }
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute(Name = "project_id")] Guid projectId, [FromQuery] string status = "all",
        [FromQuery(Name = "position_id")] Guid? positionId = null, [FromQuery(Name = "invited_user_id")] Guid? invitedUserId = null,
        [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
    {
        RecruitmentQuery.RequireOnly(Request, "status", "position_id", "invited_user_id", "page", "page_size");
        return Ok(new { success = true, message = "Project invitations retrieved.", data = await service.GetForLeaderAsync(UserId, projectId, new(status, positionId, invitedUserId, Page: page, PageSize: pageSize), ct) });
    }
    [HttpPost("{invitation_id:guid}/cancel")]
    public async Task<IActionResult> Cancel([FromRoute(Name = "project_id")] Guid projectId, [FromRoute(Name = "invitation_id")] Guid invitationId, CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request);
        return Ok(new { success = true, message = "Project invitation cancelled.", data = await service.ChangeAsync(UserId, projectId, invitationId, "cancel", ct) });
    }
}
