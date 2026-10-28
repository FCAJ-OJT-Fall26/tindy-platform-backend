using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Tindy.Api.Authorization;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]
public sealed class AiController(IAiProfileService service) : ControllerBase
{
    [HttpPost("extract-profile")]
    [EnableRateLimiting("ai-extract-profile")]
    [RequestSizeLimit(AiProfileLimits.MaxRequestBytes)]
    public async Task<IActionResult> ExtractProfile()
    {
        // No bound action parameters: MVC's form value provider would otherwise
        // consume the body before the explicit bounded multipart reader below.
        var cancellationToken = HttpContext.RequestAborted;
        Response.Headers.CacheControl = "no-store";
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new AiException(AiFailure.UnsupportedMedia);
        if (Request.ContentLength > AiProfileLimits.MaxRequestBytes)
            throw new AiException(AiFailure.PayloadTooLarge);

        // Bound the entire body, including multipart overhead and chunked requests.
        // Avoid binding IFormFile before our authorization and size checks run.
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        try
        {
            while ((read = await Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + read > AiProfileLimits.MaxRequestBytes)
                    throw new AiException(AiFailure.PayloadTooLarge);
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw new AiException(AiFailure.PayloadTooLarge);
        }

        body.Position = 0;
        var originalBody = Request.Body;
        Request.Body = body;
        try
        {
            var formFeature = new FormFeature(Request, new FormOptions
            {
                MemoryBufferThreshold = AiProfileLimits.MaxRequestBytes,
                MultipartBodyLengthLimit = AiProfileLimits.MaxRequestBytes,
                ValueLengthLimit = AiProfileLimits.MaxRequestBytes,
                ValueCountLimit = 4,
                KeyLengthLimit = 32,
                MultipartHeadersLengthLimit = 4096
            });
            HttpContext.Features.Set<IFormFeature>(formFeature);
            IFormCollection form;
            try
            {
                form = await Request.ReadFormAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                throw Invalid("request", "The multipart form is invalid.");
            }

            if (form.Keys.Any(key => key != "text") || form["text"].Count > 1 ||
                form.Files.Count > 1 || form.Files.Any(file => file.Name != "file"))
                throw Invalid("request", "Only one text field and one file field are allowed.");

            var text = form["text"].ToString();
            if (text.Length > AiProfileLimits.MaxTextLength)
                throw Invalid("text", "Text must not exceed 20000 characters.");
            text = text.Trim();
            byte[]? pdf = null;
            if (form.Files.Count == 1)
            {
                var file = form.Files[0];
                if (file.Length > AiProfileLimits.MaxPdfBytes)
                    throw new AiException(AiFailure.PayloadTooLarge);
                if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                    throw new AiException(AiFailure.UnsupportedMedia);
                if (file.Length == 0)
                    throw Invalid("file", "The PDF must not be empty.");
                using var pdfStream = new MemoryStream((int)file.Length);
                await file.CopyToAsync(pdfStream, cancellationToken);
                pdf = pdfStream.ToArray();
                if (!pdf.AsSpan().StartsWith("%PDF-"u8))
                    throw new AiException(AiFailure.DocumentRejected);
            }
            if (text.Length == 0 && pdf is null)
                throw Invalid("request", "Provide text and/or a PDF file.");

            var draft = await service.ExtractAsync(new AiProfileInput(text.Length == 0 ? null : text, pdf), cancellationToken);
            if (HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() && draft.Diagnostics is not null)
            {
                Response.Headers["X-AI-Diagnostics"] = System.Text.Json.JsonSerializer.Serialize(draft.Diagnostics);
                Response.Headers["Access-Control-Expose-Headers"] = "X-AI-Diagnostics";
            }
            return Ok(new { success = true, message = "Profile extracted. Review the suggestions before saving.", data = draft });
        }
        finally
        {
            Request.Body = originalBody;
        }
    }

    private static ProfileValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
