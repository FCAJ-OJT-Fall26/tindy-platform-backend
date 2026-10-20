using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users/{user_id:guid}/profile/certificates")]
public sealed class MemberCertificatesController : ControllerBase
{
    private readonly ICertificateService _service;
    public MemberCertificatesController(ICertificateService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromRoute(Name = "user_id")] Guid userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var callerId) || callerId == Guid.Empty)
            throw new InvalidCredentialsException();
        var data = await _service.ListMemberCertificatesAsync(userId, cancellationToken);
        return Ok(new { success = true, message = "Member certificates retrieved successfully.", data });
    }
}
