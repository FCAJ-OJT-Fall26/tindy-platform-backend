using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Route("api/projects/{project_id}/positions")]
public sealed class ProjectPositionsController : ControllerBase
{
    private readonly IProjectPositionService _positionService;

    public ProjectPositionsController(IProjectPositionService positionService)
    {
        _positionService = positionService;
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

    private static Guid ParseProjectId(string projectIdStr)
    {
        if (!Guid.TryParse(projectIdStr, out var id))
            throw new ProjectException(404, "PROJECT_NOT_FOUND", $"Project '{projectIdStr}' not found.");
        return id;
    }

    // 1. GET /api/projects/{project_id}/positions — Lấy danh sách vị trí tuyển dụng của dự án
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPositions(
        [FromRoute(Name = "project_id")] string projectId,
        CancellationToken cancellationToken)
    {
        var data = await _positionService.GetPositionsAsync(ParseProjectId(projectId), cancellationToken);
        return Ok(new { success = true, message = "Positions retrieved successfully.", data });
    }

    // 2. POST /api/projects/{project_id}/positions — Thêm vị trí tuyển dụng mới vào dự án
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreatePosition(
        [FromRoute(Name = "project_id")] string projectId,
        [FromBody] CreatePositionRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _positionService.CreatePositionAsync(CurrentUserId, ParseProjectId(projectId), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new { success = true, message = "Position added to project successfully.", data });
    }

    // 3. PUT /api/projects/{project_id}/positions/{position_id} — Sửa vị trí / đổi trạng thái
    [HttpPut("{position_id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdatePosition(
        [FromRoute(Name = "project_id")] string projectId,
        [FromRoute(Name = "position_id")] Guid positionId,
        [FromBody] UpdatePositionRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _positionService.UpdatePositionAsync(CurrentUserId, ParseProjectId(projectId), positionId, request, cancellationToken);
        return Ok(new { success = true, message = "Position updated successfully.", data });
    }

    // 4. DELETE /api/projects/{project_id}/positions/{position_id} — Xóa vị trí tuyển dụng
    [HttpDelete("{position_id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePosition(
        [FromRoute(Name = "project_id")] string projectId,
        [FromRoute(Name = "position_id")] Guid positionId,
        CancellationToken cancellationToken)
    {
        await _positionService.DeletePositionAsync(CurrentUserId, ParseProjectId(projectId), positionId, cancellationToken);
        return Ok(new { success = true, message = "Position deleted successfully." });
    }

    // 5. POST /api/projects/{project_id}/positions/{position_id}/skills — Gán kỹ năng cho vị trí
    [HttpPost("{position_id:guid}/skills")]
    [Authorize]
    public async Task<IActionResult> AddPositionSkill(
        [FromRoute(Name = "project_id")] string projectId,
        [FromRoute(Name = "position_id")] Guid positionId,
        [FromBody] AddPositionSkillRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _positionService.AddPositionSkillAsync(CurrentUserId, ParseProjectId(projectId), positionId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new { success = true, message = "Skill linked to position successfully.", data });
    }

    // 6. DELETE /api/projects/{project_id}/positions/{position_id}/skills/{skill_id} — Gỡ kỹ năng khỏi vị trí
    [HttpDelete("{position_id:guid}/skills/{skill_id:guid}")]
    [Authorize]
    public async Task<IActionResult> RemovePositionSkill(
        [FromRoute(Name = "project_id")] string projectId,
        [FromRoute(Name = "position_id")] Guid positionId,
        [FromRoute(Name = "skill_id")] Guid skillId,
        CancellationToken cancellationToken)
    {
        await _positionService.RemovePositionSkillAsync(CurrentUserId, ParseProjectId(projectId), positionId, skillId, cancellationToken);
        return Ok(new { success = true, message = "Skill removed from position successfully." });
    }
}
