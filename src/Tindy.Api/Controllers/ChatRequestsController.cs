using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController, Authorize, Route("api/chat")]
public sealed class ChatRequestsController(IChatRequests requests) : ControllerBase
{
    private Guid UserId => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty ? id : throw new InvalidCredentialsException();
    private OkObjectResult Result(object data) => Ok(new { success = true, message = "Chat updated.", data });
    [HttpGet("request-contexts")]
    public async Task<IActionResult> Contexts([FromQuery(Name = "user_id")] Guid? target, CancellationToken ct) => Result(await requests.ContextsAsync(UserId, target, ct));
    [HttpGet("requests")]
    public async Task<IActionResult> List([FromQuery] string direction = "received", [FromQuery] bool history = false, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await requests.ListAsync(UserId, direction, history, page, ct));
    [HttpPost("requests")]
    public async Task<IActionResult> Create(CreateChatRequest input, CancellationToken ct) => Result(await requests.CreateAsync(UserId, input, ct));
    [HttpPost("requests/{id:guid}/{operation}")]
    public async Task<IActionResult> Act(Guid id, string operation, CancellationToken ct) => Result(await requests.ActAsync(UserId, id, operation, ct));
    [HttpGet("blocks")]
    public async Task<IActionResult> Blocks(CancellationToken ct) => Result(await requests.BlocksAsync(UserId, ct));
    [HttpPost("conversations/{id:guid}/context")]
    public async Task<IActionResult> Share(Guid id, ShareChatContextRequest input, CancellationToken ct) => Result(await requests.ShareContextAsync(UserId, id, input, ct));
    [HttpPut("blocks/{id:guid}")]
    public async Task<IActionResult> Block(Guid id, CancellationToken ct) { await requests.SetBlockAsync(UserId, id, true, ct); return Result(new { blocked = true }); }
    [HttpDelete("blocks/{id:guid}")]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken ct) { await requests.SetBlockAsync(UserId, id, false, ct); return Result(new { blocked = false }); }
}
