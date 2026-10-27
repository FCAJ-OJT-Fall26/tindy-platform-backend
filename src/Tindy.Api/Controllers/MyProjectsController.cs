using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/projects")]
public sealed class MyProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public MyProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    private Guid CurrentUserId
    {
        get
        {
            if (Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty)
                return id;
            throw new InvalidCredentialsException();
        }
    }

    // GET /api/me/projects - Danh sách dự án tôi làm Leader hoặc đang tham gia làm Member
    [HttpGet]
    public async Task<IActionResult> GetMyProjects(CancellationToken cancellationToken)
    {
        var data = await _projectService.GetMyProjectsAsync(CurrentUserId, cancellationToken);
        return Ok(new { success = true, message = "User projects retrieved successfully.", data });
    }
}
