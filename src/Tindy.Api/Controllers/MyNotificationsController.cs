using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/notifications")]
public sealed class MyNotificationsController(INotificationService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string filter = "all", [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken ct = default)
    {
        RecruitmentQuery.RequireOnly(Request, "filter", "page", "page_size");
        return Ok(new { success = true, message = "Notifications retrieved.", data = await service.GetMineAsync(UserId, new(filter, page, pageSize), ct) });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request);
        return Ok(new { success = true, message = "Unread notification count retrieved.", data = await service.CountUnreadAsync(UserId, ct) });
    }

    [HttpPatch("{notification_id:guid}/read")]
    public async Task<IActionResult> Read([FromRoute(Name = "notification_id")] Guid notificationId, CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request);
        return Ok(new { success = true, message = "Notification marked as read.", data = await service.MarkReadAsync(UserId, notificationId, ct) });
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        RecruitmentQuery.RequireOnly(Request);
        return Ok(new { success = true, message = "Notifications marked as read.", data = await service.MarkAllReadAsync(UserId, ct) });
    }
}
