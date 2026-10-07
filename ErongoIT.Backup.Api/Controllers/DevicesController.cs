using ErongoIT.Backup.Application.Devices;
using Microsoft.AspNetCore.Authorization;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceService _deviceService;

    public DevicesController(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    [HttpGet("customers/{customerId:guid}/devices")]
    public async Task<ActionResult<IReadOnlyList<Device>>> GetByCustomer(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var devices = await _deviceService.GetByCustomerIdAsync(
            customerId,
            cancellationToken);

        return Ok(devices);
    }

    [HttpGet("devices/{id:guid}")]
    public async Task<ActionResult<Device>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var device = await _deviceService.GetByIdAsync(
            id,
            cancellationToken);

        if (device is null)
            return NotFound();

        return Ok(device);
    }

    [HttpPost("customers/{customerId:guid}/devices")]
    public async Task<ActionResult<Device>> Create(
        Guid customerId,
        [FromBody] CreateDeviceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceService.CreateAsync(
                customerId,
                request.Name,
                request.Hostname,
                request.OperatingSystem,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = device.Id },
                device);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Called once by the installer (signed in as an admin user) to
    /// register this PC. Returns the device's secret key, which the
    /// installer stores encrypted on the PC. Admin credentials are not kept.
    /// </summary>
    [HttpPost("devices/enroll")]
    public async Task<ActionResult<EnrollDeviceResponse>> Enroll(
        [FromBody] EnrollDeviceRequest request,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole("device"))
            return Forbid();

        try
        {
            var result = await _deviceService.EnrollAsync(
                request.CustomerId,
                request.Name,
                request.Hostname,
                request.OperatingSystem,
                request.BackupPlanId,
                cancellationToken);

            return Ok(new EnrollDeviceResponse(
                result.Device.Id,
                result.Device.CustomerId,
                result.Device.Name,
                result.Device.AssignedBackupPlanId,
                result.ApiKey,
                result.IsNewDevice));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    /// <summary>Invalidates a device's key (e.g. PC lost or replaced).</summary>
    [HttpPost("devices/{id:guid}/revoke-key")]
    public async Task<IActionResult> RevokeKey(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole("device"))
            return Forbid();

        var updated = await _deviceService.RevokeApiKeyAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPut("devices/{id:guid}/name")]
    public async Task<IActionResult> Rename(
        Guid id,
        [FromBody] RenameDeviceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _deviceService.RenameAsync(
                id,
                request.Name,
                cancellationToken);

            if (!updated)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPut("devices/{id:guid}/backup-plan")]
    public async Task<IActionResult> AssignBackupPlan(
        Guid id,
        [FromBody] AssignBackupPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _deviceService.AssignBackupPlanAsync(
                id,
                request.BackupPlanId,
                cancellationToken);

            if (!updated)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpDelete("devices/{id:guid}/backup-plan")]
    public async Task<IActionResult> UnassignBackupPlan(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _deviceService.UnassignBackupPlanAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    /// <summary>"Back up now" for a device (from the Agent GUI or the portal).</summary>
    [HttpPost("devices/{id:guid}/backup-now")]
    public async Task<IActionResult> BackupNow(
        Guid id,
        CancellationToken cancellationToken)
    {
        // A device may only ask for its own backup.
        if (User.IsInRole("device") &&
            !string.Equals(User.FindFirst("device_id")?.Value, id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var updated = await _deviceService.RequestBackupAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return Accepted();
    }

    [HttpPost("devices/{id:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(
        Guid id,
        [FromBody] HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _deviceService.RecordHeartbeatAsync(
            id,
            request.AgentVersion,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("devices/{id:guid}/activate")]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _deviceService.ActivateAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("devices/{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _deviceService.DeactivateAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }
}

public sealed record CreateDeviceRequest(
    string Name,
    string? Hostname,
    string? OperatingSystem);

public sealed record RenameDeviceRequest(
    string Name);

public sealed record AssignBackupPlanRequest(
    Guid BackupPlanId);

public sealed record HeartbeatRequest(
    string? AgentVersion);

public sealed record EnrollDeviceRequest(
    Guid CustomerId,
    string Name,
    string? Hostname,
    string? OperatingSystem,
    Guid? BackupPlanId);

public sealed record EnrollDeviceResponse(
    Guid DeviceId,
    Guid CustomerId,
    string Name,
    Guid? AssignedBackupPlanId,
    string DeviceKey,
    bool IsNewDevice);
