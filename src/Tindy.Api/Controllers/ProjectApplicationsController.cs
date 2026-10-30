using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{project_id:guid}/applications")]
public sealed class ProjectApplicationsController(ProjectApplicationService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpPost]
    [EnableRateLimiting("project-applications")]
    public async Task<IActionResult> Apply([FromRoute(Name = "project_id")] Guid projectId, [FromBody] CreateProjectApplicationRequest request, CancellationToken ct)
    {
        var data = await service.ApplyAsync(UserId, projectId, request, ct);
        return StatusCode(201, new { success = true, message = "Application submitted.", data });
    }
    [HttpGet]
    public async Task<IActionResult> Review([FromRoute(Name = "project_id")] Guid projectId,
        [FromQuery(Name = "position_id")] Guid? positionId = null, [FromQuery] string status = "pending",
        [FromQuery(Name = "sort_by")] string sortBy = "match", [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
        => Ok(new { success = true, message = "Project applications retrieved.", data = await service.ReviewAsync(UserId, projectId, new(positionId, status, sortBy, page, pageSize), ct) });
    [HttpPost("{application_id:guid}/accept")]
    public async Task<IActionResult> Accept([FromRoute(Name = "project_id")] Guid projectId, [FromRoute(Name = "application_id")] Guid applicationId, CancellationToken ct)
        => Ok(new { success = true, message = "Application accepted.", data = await service.AcceptAsync(UserId, projectId, applicationId, ct) });
    [HttpPost("{application_id:guid}/reject")]
    public async Task<IActionResult> Reject([FromRoute(Name = "project_id")] Guid projectId, [FromRoute(Name = "application_id")] Guid applicationId, CancellationToken ct)
        => Ok(new { success = true, message = "Application rejected.", data = await service.RejectAsync(UserId, projectId, applicationId, ct) });
}
