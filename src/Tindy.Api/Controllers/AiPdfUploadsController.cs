using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Api.Authorization;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Media;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
[Route("api/ai/pdf-uploads")]
public sealed class AiPdfUploadsController(AiPdfQuarantine quarantine) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("ai-extract-profile")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AiProfileLimits.MaxQuarantinePdfBytes + 16_384)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (file is null || file.Length == 0 || (await Request.ReadFormAsync(ct)).Files.Count != 1)
            throw new MediaRequestException(400, "AI_PDF_REQUIRED", "Upload one PDF file.");
        if (file.Length > AiProfileLimits.MaxQuarantinePdfBytes)
            throw new AiException(AiFailure.PayloadTooLarge);
        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new MediaRequestException(415, "AI_PDF_UNSUPPORTED_MEDIA", "Only PDF files are accepted.");
        await using var input = file.OpenReadStream();
        var data = await quarantine.UploadAsync(CallerId(), input, ct);
        return AcceptedAtAction(nameof(GetStatus), new { uploadId = data.UploadId },
            new { success = true, message = "PDF accepted for scanning.", data });
    }

    [HttpGet("{uploadId:guid}")]
    public async Task<IActionResult> GetStatus(Guid uploadId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var data = await quarantine.GetAsync(CallerId(), uploadId, ct)
            ?? throw new MediaRequestException(404, "AI_PDF_NOT_FOUND", "PDF upload not found.");
        return Ok(new { success = true, message = "PDF scan status retrieved.", data });
    }

    private Guid CallerId() => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
        ? id : throw new InvalidCredentialsException();
}
