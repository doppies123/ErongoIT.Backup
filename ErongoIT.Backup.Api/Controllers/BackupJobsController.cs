using ErongoIT.Backup.Application.BackupJobs;
using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class BackupJobsController : ControllerBase
{
    private readonly IBackupJobService _service;
    private readonly IBackupSnapshotService _snapshotService;

    public BackupJobsController(
        IBackupJobService service,
        IBackupSnapshotService snapshotService)
    {
        _service = service;
        _snapshotService = snapshotService;
    }

    [HttpGet("devices/{deviceId:guid}/backup-jobs")]
    public async Task<ActionResult<IReadOnlyList<BackupJobResponse>>> GetByDevice(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var jobs = await _service.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);

        var jobIds = jobs
            .Select(x => x.Id)
            .ToList();

        var fileCounts =
            await _snapshotService.GetFileCountsByBackupJobIdsAsync(
                jobIds,
                cancellationToken);

        var response = jobs
            .Select(job =>
                new BackupJobResponse(
                    job,
                    fileCounts.TryGetValue(
                        job.Id,
                        out var count)
                        ? count
                        : 0))
            .ToList();

        return Ok(response);
    }

    [HttpGet("devices/{deviceId:guid}/restore-points")]
    public async Task<ActionResult<IReadOnlyList<BackupJob>>> GetRestorePoints(
        Guid deviceId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var restorePoints =
                await _service.GetRestorePointsByDeviceIdAsync(
                    deviceId,
                    fromUtc,
                    toUtc,
                    cancellationToken);

            return Ok(restorePoints);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpGet("backup-jobs/{id:guid}")]
    public async Task<ActionResult<BackupJob>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await _service.GetByIdAsync(
            id,
            cancellationToken);

        if (job is null)
            return NotFound();

        return Ok(job);
    }

    [HttpPost("customers/{customerId:guid}/backup-jobs")]
    public async Task<ActionResult<BackupJob>> Create(
        Guid customerId,
        [FromBody] CreateBackupJobRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await _service.CreateAsync(
                customerId,
                request.DeviceId,
                request.BackupPlanId,
                request.Type,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = job.Id },
                job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("backup-jobs/{id:guid}/start")]
    public async Task<IActionResult> Start(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.StartAsync(
                id,
                cancellationToken);

            return result
                ? NoContent()
                : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("backup-jobs/{id:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody] CompleteBackupJobRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CompleteAsync(
                id,
                request.BytesSelected,
                request.BytesUploaded,
                cancellationToken);

            return result
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("backup-jobs/{id:guid}/fail")]
    public async Task<IActionResult> Fail(
        Guid id,
        [FromBody] FailBackupJobRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.FailAsync(
                id,
                request.ErrorMessage,
                cancellationToken);

            return result
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("backup-jobs/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CancelAsync(
                id,
                cancellationToken);

            return result
                ? NoContent()
                : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("devices/{deviceId:guid}/backup-jobs/clear-history")]
    public async Task<IActionResult> ClearHistory(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _service.ClearHistoryAsync(
                deviceId,
                cancellationToken);

            return Ok(new
            {
                deviceId,
                deleted
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("devices/{deviceId:guid}/backup-jobs/recover-stale")]
    public async Task<IActionResult> RecoverStale(
        Guid deviceId,
        [FromQuery] int timeoutMinutes = 30,
        CancellationToken cancellationToken = default)
    {
        if (timeoutMinutes <= 0)
        {
            return BadRequest(new
            {
                error = "Timeout must be greater than zero minutes."
            });
        }

        try
        {
            var recovered = await _service.RecoverStaleJobsAsync(
                deviceId,
                TimeSpan.FromMinutes(timeoutMinutes),
                cancellationToken);

            return Ok(new
            {
                deviceId,
                timeoutMinutes,
                recovered
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }
}

public sealed record CreateBackupJobRequest(
    Guid DeviceId,
    Guid BackupPlanId,
    BackupType Type);

public sealed record CompleteBackupJobRequest(
    long BytesSelected,
    long BytesUploaded);

public sealed record FailBackupJobRequest(
    string ErrorMessage);

public sealed record BackupJobResponse(
    Guid Id,
    Guid CustomerId,
    Guid DeviceId,
    Guid BackupPlanId,
    BackupType Type,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long BytesSelected,
    long BytesUploaded,
    int FilesUploaded,
    string Status,
    string? ErrorMessage)
{
    public BackupJobResponse(
        BackupJob job,
        int filesUploaded)
        : this(
            job.Id,
            job.CustomerId,
            job.DeviceId,
            job.BackupPlanId,
            job.Type,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.BytesSelected,
            job.BytesUploaded,
            filesUploaded,
            job.Status,
            job.ErrorMessage)
    {
    }
}
