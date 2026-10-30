using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/discovery/projects")]
public sealed class DiscoveryController(DiscoveryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 20, [FromQuery] string? cursor = null,
        [FromQuery] string? category = null, [FromQuery(Name = "role_id")] Guid? roleId = null,
        [FromQuery(Name = "skill_id")] Guid? skillId = null, CancellationToken ct = default)
    {
        var data = await service.GetAsync(Guid.Parse(User.FindFirst("sub")!.Value), limit, cursor, new(category, roleId, skillId), ct);
        return Ok(new { success = true, message = "Discovery projects retrieved successfully.", data });
    }
}
