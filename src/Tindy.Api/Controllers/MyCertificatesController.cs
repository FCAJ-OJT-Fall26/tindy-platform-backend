using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

// DefaultPolicy: JWT + active + completed onboarding; not AuthenticatedOnly.
[ApiController]
[Authorize]
[Route("api/me/profile/certificates")]
public sealed class MyCertificatesController : ControllerBase
{
    private readonly ICertificateService _service;
    public MyCertificatesController(ICertificateService service) => _service = service;

    private Guid CurrentUserId
    {
        get
        {
            if (Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty)
                return id;
            throw new InvalidCredentialsException();
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var data = await _service.ListMyCertificatesAsync(CurrentUserId, cancellationToken);
        return Ok(new { success = true, message = "Certificates retrieved successfully.", data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement json, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        var command = CertificateRequestParser.Parse(json);
        var data = await _service.CreateAsync(userId, command, cancellationToken);
        // No GET-by-id contract, so do not create a Location pointing to a missing endpoint.
        return StatusCode(StatusCodes.Status201Created,
            new { success = true, message = "Certificate created successfully.", data });
    }

    [HttpPut("{certificate_id:guid}")]
    public async Task<IActionResult> Update([FromRoute(Name = "certificate_id")] Guid certificateId,
        [FromBody] JsonElement json, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        var command = CertificateRequestParser.Parse(json);
        var data = await _service.UpdateAsync(userId, certificateId, command, cancellationToken);
        return Ok(new { success = true, message = "Certificate updated successfully.", data });
    }

    [HttpDelete("{certificate_id:guid}")]
    public async Task<IActionResult> Delete([FromRoute(Name = "certificate_id")] Guid certificateId,
        CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(CurrentUserId, certificateId, cancellationToken);
        return NoContent();
    }
}
