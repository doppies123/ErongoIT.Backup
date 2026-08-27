using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Domain.Enums;

namespace ErongoIT.Backup.Application.BackupPlans;

public interface IBackupPlanService
{
    Task<IReadOnlyList<BackupPlan>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<BackupPlan?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<BackupPlan> CreateAsync(
        Guid customerId,
        string name,
        BackupScheduleType scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateScheduleAsync(
        Guid id,
        BackupScheduleType scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays,
        CancellationToken cancellationToken = default);

    Task<bool> EnableAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> DisableAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
