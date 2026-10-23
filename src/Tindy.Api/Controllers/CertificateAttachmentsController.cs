using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/profile/certificates/{certificate_id:guid}/attachments")]
public sealed class CertificateAttachmentsController(ICertificateAttachmentService service) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("media-certificate-upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> Upload([FromRoute(Name = "certificate_id")] Guid certificateId, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null) throw new MediaRequestException(400, "MEDIA_FILE_REQUIRED", "A file is required.");
        await using var input = file.OpenReadStream();
        var data = await service.UploadAsync(CallerId(), certificateId, input, file.FileName, file.Length, ct);
        return StatusCode(data.Status == "processing" ? StatusCodes.Status202Accepted : StatusCodes.Status201Created,
            new { success = true, message = data.Status == "processing" ? "Attachment accepted for processing." : "Attachment uploaded successfully.", data });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromRoute(Name = "certificate_id")] Guid certificateId, CancellationToken ct)
    {
        var ownerId = CallerId();
        var data = await service.ListAsync(ownerId, ownerId, certificateId, ct);
        return Ok(new { success = true, message = "Attachments retrieved successfully.", data });
    }

    [HttpPatch("{attachment_id:guid}/visibility")]
    [EnableRateLimiting("media-visibility")]
    public async Task<IActionResult> Visibility([FromRoute(Name = "certificate_id")] Guid certificateId,
        [FromRoute(Name = "attachment_id")] Guid attachmentId, [FromBody] AttachmentVisibilityRequest request, CancellationToken ct)
    {
        var data = await service.ChangeVisibilityAsync(CallerId(), certificateId, attachmentId, request.Visibility, ct);
        return Ok(new { success = true, message = "Attachment visibility updated successfully.", data });
    }

    [HttpDelete("{attachment_id:guid}")]
    public async Task<IActionResult> Delete([FromRoute(Name = "certificate_id")] Guid certificateId,
        [FromRoute(Name = "attachment_id")] Guid attachmentId, CancellationToken ct)
    {
        await service.DeleteAsync(CallerId(), certificateId, attachmentId, ct);
        return NoContent();
    }

    private Guid CallerId() => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty ? id : throw new InvalidCredentialsException();
}

[ApiController]
[Authorize]
public sealed class CertificateAttachmentReadsController(ICertificateAttachmentService service) : ControllerBase
{
    [HttpGet("api/me/profile/media/quota")]
    public async Task<IActionResult> Quota(CancellationToken ct)
    {
        var data = await service.GetQuotaAsync(CallerId(), ct);
        return Ok(new { success = true, message = "Media quota retrieved successfully.", data });
    }

    [HttpGet("api/users/{user_id:guid}/profile/certificates/{certificate_id:guid}/attachments")]
    public async Task<IActionResult> MemberList([FromRoute(Name = "user_id")] Guid userId,
        [FromRoute(Name = "certificate_id")] Guid certificateId, CancellationToken ct)
    {
        var data = await service.ListAsync(CallerId(), userId, certificateId, ct);
        return Ok(new { success = true, message = "Attachments retrieved successfully.", data });
    }

    private Guid CallerId() => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty ? id : throw new InvalidCredentialsException();
}
