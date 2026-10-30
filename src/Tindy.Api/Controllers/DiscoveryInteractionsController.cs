using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/discovery")]
public sealed class DiscoveryInteractionsController(IDiscoveryInteractionRepository repository, TimeProvider clock) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet("saved")]
    public async Task<IActionResult> Saved([FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
        => Ok(new { success = true, message = "Saved projects retrieved successfully.", data = await repository.GetSavedAsync(UserId, page, pageSize, clock.GetUtcNow().UtcDateTime, ct) });

    [HttpPut("saved/{project_id:guid}")]
    public Task<IActionResult> Save([FromRoute(Name = "project_id")] Guid projectId, CancellationToken ct) => Set(projectId, true, true, ct);
    [HttpDelete("saved/{project_id:guid}")]
    public Task<IActionResult> Unsave([FromRoute(Name = "project_id")] Guid projectId, CancellationToken ct) => Set(projectId, true, false, ct);
    [HttpPut("skipped/{project_id:guid}")]
    public Task<IActionResult> Skip([FromRoute(Name = "project_id")] Guid projectId, CancellationToken ct) => Set(projectId, false, true, ct);
    [HttpDelete("skipped/{project_id:guid}")]
    public Task<IActionResult> Unskip([FromRoute(Name = "project_id")] Guid projectId, CancellationToken ct) => Set(projectId, false, false, ct);

    private async Task<IActionResult> Set(Guid projectId, bool saved, bool value, CancellationToken ct)
    {
        await repository.SetAsync(UserId, projectId, saved, value, clock.GetUtcNow().UtcDateTime, ct);
        return Ok(new { success = true, message = "Discovery preference updated.", data = new { project_id = projectId } });
    }
}
