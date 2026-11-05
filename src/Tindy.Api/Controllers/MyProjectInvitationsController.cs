using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/project-invitations")]
public sealed class MyProjectInvitationsController(ProjectInvitationService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string status = "all", [FromQuery(Name = "project_id")] Guid? projectId = null,
        [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
    {
        RecruitmentQuery.RequireOnly(Request, "status", "project_id", "page", "page_size");
        return Ok(new { success = true, message = "My project invitations retrieved.", data = await service.GetMineAsync(UserId, new(status, ProjectId: projectId, Page: page, PageSize: pageSize), ct) });
    }
    [HttpPost("{invitation_id:guid}/accept")]
    public Task<IActionResult> Accept([FromRoute(Name = "invitation_id")] Guid invitationId, CancellationToken ct) => Change(invitationId, "accept", ct);
    [HttpPost("{invitation_id:guid}/decline")]
    public Task<IActionResult> Decline([FromRoute(Name = "invitation_id")] Guid invitationId, CancellationToken ct) => Change(invitationId, "decline", ct);
    private async Task<IActionResult> Change(Guid id, string action, CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request);
        return Ok(new { success = true, message = action == "accept" ? "Project invitation accepted." : "Project invitation declined.", data = await service.ChangeAsync(UserId, null, id, action, ct) });
    }
}
