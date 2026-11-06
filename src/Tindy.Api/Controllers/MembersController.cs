using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class MembersController(IProfileService profiles) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q = null, [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var caller) || caller == Guid.Empty)
            throw new InvalidCredentialsException();
        var data = await profiles.SearchMembersAsync(q, page, pageSize, cancellationToken);
        return Ok(new { success = true, message = "Members retrieved successfully.", data });
    }
}
