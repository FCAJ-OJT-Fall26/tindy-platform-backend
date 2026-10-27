using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
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

    // POST /api/projects - Tạo dự án mới kèm danh sách vị trí
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateProject(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _projectService.CreateProjectAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new { success = true, message = "Project created successfully. Recruitment is open.", data });
    }

    // GET /api/projects - Danh sách dự án (phân trang, lọc, tìm kiếm)
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetProjects(
        [FromQuery] GetProjectsQuery query,
        CancellationToken cancellationToken)
    {
        var data = await _projectService.GetProjectsAsync(query, cancellationToken);
        return Ok(new { success = true, message = "Projects retrieved successfully.", data });
    }

    // GET /api/projects/{project_id} - Chi tiết dự án
    [HttpGet("{project_id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProjectById(
        [FromRoute(Name = "project_id")] string projectIdStr,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(projectIdStr, out var projectId))
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", $"Project '{projectIdStr}' not found.");
        }

        var data = await _projectService.GetProjectByIdAsync(projectId, cancellationToken);
        return Ok(new { success = true, message = "Project details retrieved successfully.", data });
    }

    // PUT /api/projects/{project_id} - Cập nhật dự án (Chỉ Leader)
    [HttpPut("{project_id}")]
    [Authorize]
    public async Task<IActionResult> UpdateProject(
        [FromRoute(Name = "project_id")] string projectIdStr,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(projectIdStr, out var projectId))
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", $"Project '{projectIdStr}' not found.");
        }

        var data = await _projectService.UpdateProjectAsync(CurrentUserId, projectId, request, cancellationToken);
        return Ok(new { success = true, message = "Project updated successfully.", data });
    }

    // DELETE /api/projects/{project_id} - Lưu trữ / đóng dự án (Soft delete, Chỉ Leader)
    [HttpDelete("{project_id}")]
    [Authorize]
    public async Task<IActionResult> DeleteProject(
        [FromRoute(Name = "project_id")] string projectIdStr,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(projectIdStr, out var projectId))
        {
            throw new ProjectException(404, "PROJECT_NOT_FOUND", $"Project '{projectIdStr}' not found.");
        }

        await _projectService.DeleteProjectAsync(CurrentUserId, projectId, cancellationToken);
        return Ok(new
        {
            success = true,
            message = "Project archived successfully.",
            data = new { project_id = projectId, status = "archived" }
        });
    }
}
