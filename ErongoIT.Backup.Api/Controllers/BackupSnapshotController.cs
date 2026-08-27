using ErongoIT.Backup.Application.BackupJobs;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Route("api/backup-jobs")]
public sealed class BackupSnapshotController : ControllerBase
{
    private readonly IBackupSnapshotService _service;

    public BackupSnapshotController(
        IBackupSnapshotService service)
    {
        _service = service;
    }

    [HttpGet("{backupJobId:guid}/files")]
    public async Task<ActionResult<IReadOnlyList<BackupFile>>> GetFiles(
        Guid backupJobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await _service.GetFilesAsync(
                backupJobId,
                cancellationToken);

            return Ok(files);
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

    [HttpGet("{backupJobId:guid}/files/{backupFileId:guid}/download")]
    public async Task<IActionResult> DownloadFile(
        Guid backupJobId,
        Guid backupFileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.OpenFileAsync(
                backupJobId,
                backupFileId,
                cancellationToken);

            if (result is null)
                return NotFound(new
                {
                    error = "Backup file was not found."
                });

            return File(
                result.Content,
                "application/octet-stream",
                result.FileName,
                enableRangeProcessing: true);
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

    [HttpPost("{backupJobId:guid}/restore")]
    public async Task<ActionResult<BackupRestoreResult>> Restore(
        Guid backupJobId,
        [FromBody] RestoreBackupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RestoreAsync(
                backupJobId,
                request.DestinationPath,
                cancellationToken);

            return Ok(result);
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

    [HttpPost("{backupJobId:guid}/files")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<ActionResult<BackupFile>> StoreFile(
        Guid backupJobId,
        [FromQuery] Guid customerId,
        [FromQuery] Guid deviceId,
        [FromQuery] string relativePath,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new
            {
                error = "A non-empty file is required."
            });
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return BadRequest(new
            {
                error = "Relative path is required."
            });
        }

        await using var stream = file.OpenReadStream();

        try
        {
            var backupFile = await _service.StoreFileAsync(
                customerId,
                deviceId,
                backupJobId,
                relativePath,
                stream,
                cancellationToken);

            return Ok(backupFile);
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
}

public sealed record RestoreBackupRequest(
    string DestinationPath);
