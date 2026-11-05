using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{project_id:guid}/candidate-interactions")]
public sealed class ProjectCandidateInteractionsController(TeammateDiscoveryInteractionService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute(Name = "project_id")] Guid projectId, [FromQuery(Name = "position_id")] Guid positionId,
        [FromQuery] string? action = null, [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
    {
        RecruitmentQuery.RequireOnly(Request, "position_id", "action", "page", "page_size");
        return Ok(new { success = true, message = "Candidate interactions retrieved.", data = await service.GetAsync(UserId, projectId, positionId, action, page, pageSize, ct) });
    }
    [HttpPut("{candidate_user_id:guid}")]
    public async Task<IActionResult> Put([FromRoute(Name = "project_id")] Guid projectId, [FromRoute(Name = "candidate_user_id")] Guid candidateId,
        [FromQuery(Name = "position_id")] Guid positionId, [FromBody] JsonElement json, CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request, "position_id");
        return Ok(new { success = true, message = "Candidate interaction updated.", data = await service.SetAsync(UserId, projectId, positionId, candidateId, SetTeammateInteractionRequest.Parse(json), ct) });
    }
    [HttpDelete("{candidate_user_id:guid}")]
    public async Task<IActionResult> Delete([FromRoute(Name = "project_id")] Guid projectId, [FromRoute(Name = "candidate_user_id")] Guid candidateId,
        [FromQuery(Name = "position_id")] Guid positionId, CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request, "position_id");
        await service.ResetAsync(UserId, projectId, positionId, candidateId, ct);
        return Ok(new { success = true, message = "Candidate interaction reset.", data = new ResetTeammateInteractionDto(projectId, positionId, candidateId) });
    }
}
