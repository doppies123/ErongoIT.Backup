using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Domain.Enums;

namespace ErongoIT.Backup.Application.BackupPlans;

public sealed class BackupPlanService : IBackupPlanService
{
    private readonly IBackupPlanRepository _plans;
    private readonly ICustomerRepository _customers;

    public BackupPlanService(
        IBackupPlanRepository plans,
        ICustomerRepository customers)
    {
        _plans = plans;
        _customers = customers;
    }

    public Task<IReadOnlyList<BackupPlan>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _plans.GetByCustomerIdAsync(
            customerId,
            cancellationToken);
    }

    public Task<BackupPlan?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _plans.GetByIdAsync(
            id,
            cancellationToken);
    }

    public async Task<BackupPlan> CreateAsync(
        Guid customerId,
        string name,
        BackupScheduleType scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(
            customerId,
            cancellationToken);

        if (customer is null)
            throw new InvalidOperationException(
                "Customer was not found.");

        if (await _plans.ExistsByCustomerAndNameAsync(
                customerId,
                name,
                cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                "A backup plan with this name already exists for the customer.");
        }

        var plan = new BackupPlan(
            customerId,
            name,
            scheduleType,
            intervalMinutes,
            scheduleTimeMinutes,
            scheduleDayOfWeek,
            retentionDays);

        await _plans.AddAsync(
            plan,
            cancellationToken);

        await _plans.SaveChangesAsync(
            cancellationToken);

        return plan;
    }

    public async Task<bool> UpdateScheduleAsync(
        Guid id,
        BackupScheduleType scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetByIdAsync(
            id,
            cancellationToken);

        if (plan is null)
            return false;

        plan.UpdateSchedule(
            scheduleType,
            intervalMinutes,
            scheduleTimeMinutes,
            scheduleDayOfWeek,
            retentionDays);

        await _plans.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> EnableAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetByIdAsync(
            id,
            cancellationToken);

        if (plan is null)
            return false;

        plan.Enable();

        await _plans.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> DisableAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetByIdAsync(
            id,
            cancellationToken);

        if (plan is null)
            return false;

        plan.Disable();

        await _plans.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetByIdAsync(
            id,
            cancellationToken);

        if (plan is null)
            return false;

        _plans.Remove(plan);

        await _plans.SaveChangesAsync(
            cancellationToken);

        return true;
    }
}
