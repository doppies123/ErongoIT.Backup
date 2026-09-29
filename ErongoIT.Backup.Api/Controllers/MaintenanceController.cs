using ErongoIT.Backup.Application.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/maintenance")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly IRetentionService _retention;

    public MaintenanceController(
        IRetentionService retention)
    {
        _retention = retention;
    }

    /// <summary>
    /// Runs retention cleanup now.
    /// dryRun=true (default) only reports what would be deleted.
    /// </summary>
    [HttpPost("retention")]
    public async Task<ActionResult<RetentionReport>> RunRetention(
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var report = await _retention.RunAsync(
            dryRun,
            cancellationToken);

        return Ok(report);
    }
}
