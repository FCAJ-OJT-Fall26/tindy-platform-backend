using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/project-applications")]
public sealed class MyProjectApplicationsController(ProjectApplicationService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
        => Ok(new { success = true, message = "My project applications retrieved.", data = await service.GetMineAsync(UserId, page, pageSize, ct) });
    [HttpPost("{application_id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw([FromRoute(Name = "application_id")] Guid applicationId, CancellationToken ct)
        => Ok(new { success = true, message = "Application withdrawn.", data = await service.WithdrawAsync(UserId, applicationId, ct) });
}
