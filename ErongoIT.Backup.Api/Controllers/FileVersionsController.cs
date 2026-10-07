using System.IO.Compression;
using ErongoIT.Backup.Application.FileVersions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

/// <summary>
/// CrashPlan-style backup and restore:
///   Agent:   check-content -> upload missing content -> send changes
///   Restore: browse folders "as of" a time -> resolve selection -> download versions
/// A device token may only use its own device; admins may use any device.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public sealed class FileVersionsController : ControllerBase
{
    private readonly IFileVersionService _service;

    public FileVersionsController(IFileVersionService service)
    {
        _service = service;
    }

    // ----------------------------------------------------------------
    // Backup (agent)
    // ----------------------------------------------------------------

    [HttpPost("devices/{deviceId:guid}/sync/check-content")]
    public Task<IActionResult> CheckContent(
        Guid deviceId,
        [FromBody] CheckContentRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(deviceId, async () =>
        {
            var missing = await _service.GetMissingContentAsync(
                request?.Contents ?? Array.Empty<ContentReference>(),
                cancellationToken);

            return Ok(new { missing });
        });

    [HttpPost("devices/{deviceId:guid}/sync/content")]
    [RequestSizeLimit(long.MaxValue)]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public Task<IActionResult> UploadContent(
        Guid deviceId,
        [FromQuery] string sha256,
        IFormFile file,
        CancellationToken cancellationToken,
        [FromQuery] string? encoding = null) =>
        RunAsync(deviceId, async () =>
        {
            if (file is null)
                return BadRequest(new { error = "A file is required." });

            var gzip = string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(encoding) && !gzip)
                return BadRequest(new { error = $"Unsupported encoding '{encoding}'." });

            await using var raw = file.OpenReadStream();
            await using Stream stream = gzip
                ? new GZipStream(raw, CompressionMode.Decompress)
                : raw;

            var stored = await _service.StoreContentAsync(stream, sha256, cancellationToken);

            return Ok(stored);
        });

    [HttpPost("devices/{deviceId:guid}/sync/changes")]
    public Task<IActionResult> ApplyChanges(
        Guid deviceId,
        [FromBody] ApplyChangesRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(deviceId, async () =>
        {
            if (request is null)
                return BadRequest(new { error = "A request body is required." });

            var result = await _service.ApplyChangesAsync(
                deviceId,
                request.BackupJobId,
                request.Changed ?? Array.Empty<FileChange>(),
                request.Deleted ?? Array.Empty<string>(),
                cancellationToken);

            return Ok(result);
        });

    [HttpGet("devices/{deviceId:guid}/sync/state")]
    public Task<IActionResult> GetState(
        Guid deviceId,
        [FromQuery] string? after,
        CancellationToken cancellationToken,
        [FromQuery] int take = 5000) =>
        RunAsync(deviceId, async () =>
            Ok(await _service.GetCurrentStateAsync(deviceId, after, take, cancellationToken)));

    // ----------------------------------------------------------------
    // Restore (Agent GUI / portal)
    // ----------------------------------------------------------------

    [HttpGet("devices/{deviceId:guid}/browse")]
    public Task<IActionResult> Browse(
        Guid deviceId,
        [FromQuery] string? folder,
        [FromQuery] DateTime? asOf,
        CancellationToken cancellationToken,
        [FromQuery] bool includeDeleted = false) =>
        RunAsync(deviceId, async () =>
            Ok(await _service.BrowseAsync(deviceId, folder, asOf, includeDeleted, cancellationToken)));

    [HttpPost("devices/{deviceId:guid}/restore/resolve")]
    public Task<IActionResult> Resolve(
        Guid deviceId,
        [FromBody] ResolveRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(deviceId, async () =>
            Ok(await _service.ResolveAsync(
                deviceId,
                request?.Paths ?? Array.Empty<string>(),
                request?.AsOf,
                request?.IncludeDeleted ?? false,
                cancellationToken)));

    [HttpGet("devices/{deviceId:guid}/search")]
    public Task<IActionResult> Search(
        Guid deviceId,
        [FromQuery] string q,
        [FromQuery] DateTime? asOf,
        CancellationToken cancellationToken,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int take = 500) =>
        RunAsync(deviceId, async () =>
            Ok(await _service.SearchAsync(deviceId, q, asOf, includeDeleted, take, cancellationToken)));

    [HttpGet("file-versions/{versionId:guid}/content")]
    public async Task<IActionResult> Download(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var version = await _service.OpenVersionAsync(versionId, cancellationToken);

            if (version is null)
                return NotFound();

            if (!CanAccess(version.DeviceId))
            {
                await version.Content.DisposeAsync();
                return Forbid();
            }

            return File(version.Content, "application/octet-stream", version.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ----------------------------------------------------------------

    private bool CanAccess(Guid deviceId)
    {
        if (!User.IsInRole("device"))
            return true;

        return string.Equals(
            User.FindFirst("device_id")?.Value,
            deviceId.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IActionResult> RunAsync(
        Guid deviceId,
        Func<Task<IActionResult>> action)
    {
        if (!CanAccess(deviceId))
            return Forbid();

        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { error = $"Compressed upload is corrupt: {ex.Message}" });
        }
    }
}

public sealed record CheckContentRequest(
    IReadOnlyList<ContentReference>? Contents);

public sealed record ApplyChangesRequest(
    Guid? BackupJobId,
    IReadOnlyList<FileChange>? Changed,
    IReadOnlyList<string>? Deleted);

public sealed record ResolveRequest(
    IReadOnlyList<string>? Paths,
    DateTime? AsOf,
    bool IncludeDeleted);
