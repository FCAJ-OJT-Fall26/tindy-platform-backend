using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/profile")]
public sealed class ProfileMediaController : ControllerBase
{
    private readonly IProfileMediaService _media;
    public ProfileMediaController(IProfileMediaService media) => _media = media;

    [HttpPost("avatar")]
    [EnableRateLimiting("media-avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null) throw new MediaRequestException(400, "MEDIA_FILE_REQUIRED", "A file is required.");
        await using var stream = file.OpenReadStream();
        var data = await _media.UploadAsync(CurrentUserId(), true, stream, file.FileName, file.Length, ct);
        return Ok(new { success = true, message = "Avatar updated successfully.", data });
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        await _media.RemoveAsync(CurrentUserId(), true, ct);
        return NoContent();
    }

    public sealed record AvatarSourceRequest(string Source, string? Provider);

    [HttpPut("avatar/source")]
    [EnableRateLimiting("media-avatar")]
    public async Task<IActionResult> SelectAvatar([FromBody] AvatarSourceRequest request, CancellationToken ct)
    {
        if (request is null) throw new MediaRequestException(400, "MEDIA_INVALID_SOURCE", "Avatar source is required.");
        var data = await _media.ChooseAvatarAsync(CurrentUserId(), request.Source, request.Provider, ct);
        return Ok(new { success = true, message = "Avatar source updated successfully.", data });
    }

    [HttpPost("cover-image")]
    [EnableRateLimiting("media-cover")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> UploadCover([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null) throw new MediaRequestException(400, "MEDIA_FILE_REQUIRED", "A file is required.");
        await using var stream = file.OpenReadStream();
        var data = await _media.UploadAsync(CurrentUserId(), false, stream, file.FileName, file.Length, ct);
        return Ok(new { success = true, message = "Cover image updated successfully.", data });
    }

    [HttpDelete("cover-image")]
    public async Task<IActionResult> DeleteCover(CancellationToken ct)
    {
        await _media.RemoveAsync(CurrentUserId(), false, ct);
        return NoContent();
    }

    private Guid CurrentUserId()
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var id) || id == Guid.Empty)
            throw new InvalidCredentialsException();
        return id;
    }
}

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class PrivateMediaController : ControllerBase
{
    private readonly IProfileMediaService _media;
    public PrivateMediaController(IProfileMediaService media) => _media = media;

    [HttpGet("{media_asset_id:guid}/content")]
    [EnableRateLimiting("media-read")]
    public async Task<IActionResult> Content([FromRoute(Name = "media_asset_id")] Guid id, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var content = await _media.ReadAsync(id, ct);
        // The response owns this handle; disposing here would close the stream before MVC sends it.
        HttpContext.Response.RegisterForDisposeAsync(content.Handle);
        return File(content.Handle.Content, content.ContentType, enableRangeProcessing: false);
    }
}
