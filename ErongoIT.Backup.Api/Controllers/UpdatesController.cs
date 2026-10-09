using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

/// <summary>
/// Agent updates. tools/publish-update.ps1 uploads an installer plus
/// latest.json into the updates folder on the VPS
/// (/opt/erongoit-backup/updates, mounted read-only at /app/updates).
/// The Agent GUI asks for the latest version and downloads the installer.
/// </summary>
[ApiController]
[Authorize]
[Route("api/updates")]
public sealed class UpdatesController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _root;

    public UpdatesController(IConfiguration configuration)
    {
        _root = configuration["Updates:RootPath"] ?? "/app/updates";
    }

    /// <summary>The newest published agent version, or 404 when none is published.</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<UpdateInfo>> Latest(CancellationToken cancellationToken)
    {
        var info = await ReadLatestAsync(cancellationToken);

        return info is null ? NotFound() : Ok(info);
    }

    /// <summary>Downloads the installer named in latest.json.</summary>
    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        var info = await ReadLatestAsync(cancellationToken);

        // Only the currently published installer can be downloaded:
        // no other files, no path tricks.
        if (info is null || !string.Equals(fileName, info.FileName, StringComparison.Ordinal))
            return NotFound();

        var path = Path.Combine(_root, info.FileName);

        if (!System.IO.File.Exists(path))
            return NotFound();

        return PhysicalFile(path, "application/octet-stream", info.FileName, enableRangeProcessing: true);
    }

    private async Task<UpdateInfo?> ReadLatestAsync(CancellationToken cancellationToken)
    {
        var manifest = Path.Combine(_root, "latest.json");

        if (!System.IO.File.Exists(manifest))
            return null;

        try
        {
            await using var stream = System.IO.File.OpenRead(manifest);

            var info = await JsonSerializer.DeserializeAsync<UpdateInfo>(stream, JsonOptions, cancellationToken);

            if (info is null ||
                string.IsNullOrWhiteSpace(info.Version) ||
                string.IsNullOrWhiteSpace(info.FileName) ||
                info.FileName != Path.GetFileName(info.FileName))
            {
                return null;
            }

            return info;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record UpdateInfo(
    string Version,
    string FileName,
    string Sha256,
    long SizeBytes,
    DateTime ReleasedUtc,
    string? Notes);
