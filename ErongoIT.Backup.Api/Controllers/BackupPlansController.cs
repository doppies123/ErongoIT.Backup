using ErongoIT.Backup.Application.BackupPlans;
using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class BackupPlansController : ControllerBase
{
    private readonly IBackupPlanService _backupPlanService;

    public BackupPlansController(IBackupPlanService backupPlanService)
    {
        _backupPlanService = backupPlanService;
    }

    [HttpGet("customers/{customerId:guid}/backup-plans")]
    public async Task<ActionResult<IReadOnlyList<BackupPlan>>> GetByCustomer(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var plans = await _backupPlanService.GetByCustomerIdAsync(
            customerId,
            cancellationToken);

        return Ok(plans);
    }

    [HttpGet("backup-plans/{id:guid}")]
    public async Task<ActionResult<BackupPlan>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var plan = await _backupPlanService.GetByIdAsync(
            id,
            cancellationToken);

        if (plan is null)
            return NotFound();

        return Ok(plan);
    }

    [HttpPost("customers/{customerId:guid}/backup-plans")]
    public async Task<ActionResult<BackupPlan>> Create(
        Guid customerId,
        [FromBody] CreateBackupPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var plan = await _backupPlanService.CreateAsync(
                customerId,
                request.Name,
                request.ScheduleType,
                request.IntervalMinutes,
                request.ScheduleTimeMinutes,
                request.ScheduleDayOfWeek,
                request.RetentionDays,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = plan.Id },
                plan);
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

    [HttpPut("backup-plans/{id:guid}/schedule")]
    public async Task<IActionResult> UpdateSchedule(
        Guid id,
        [FromBody] UpdateBackupPlanScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _backupPlanService.UpdateScheduleAsync(
                id,
                request.ScheduleType,
                request.IntervalMinutes,
                request.ScheduleTimeMinutes,
                request.ScheduleDayOfWeek,
                request.RetentionDays,
                cancellationToken);

            if (!updated)
                return NotFound();

            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("backup-plans/{id:guid}/enable")]
    public async Task<IActionResult> Enable(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _backupPlanService.EnableAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("backup-plans/{id:guid}/disable")]
    public async Task<IActionResult> Disable(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _backupPlanService.DisableAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("backup-plans/{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await _backupPlanService.DeleteAsync(
            id,
            cancellationToken);

        if (!deleted)
            return NotFound();

        return NoContent();
    }
}

public sealed record CreateBackupPlanRequest(
    string Name,
    BackupScheduleType ScheduleType,
    int IntervalMinutes,
    int ScheduleTimeMinutes,
    int ScheduleDayOfWeek,
    int RetentionDays);

public sealed record UpdateBackupPlanScheduleRequest(
    BackupScheduleType ScheduleType,
    int IntervalMinutes,
    int ScheduleTimeMinutes,
    int ScheduleDayOfWeek,
    int RetentionDays);
