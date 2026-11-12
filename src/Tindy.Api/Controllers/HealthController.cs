using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "Tindy API"
        });
    }

    [HttpGet("live")]
    public IActionResult Live() => Get();

    [HttpGet("ready")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ready([FromServices] TindyDbContext db, CancellationToken cancellationToken)
    {
        using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeTimeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", probeTimeout.Token);
            return Get();
        }
        catch (Exception ex) when (ex is DbException ||
            (ex is OperationCanceledException && probeTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "Unhealthy",
                service = "Tindy API"
            });
        }
    }
}
