using ErongoIT.Backup.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly BackupDbContext _db;

    public HealthController(BackupDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var databaseHealthy = await _db.Database.CanConnectAsync(
            cancellationToken);

        return Ok(new
        {
            status = databaseHealthy ? "Healthy" : "Unhealthy",
            database = databaseHealthy ? "Connected" : "Disconnected",
            timestampUtc = DateTime.UtcNow
        });
    }
}
