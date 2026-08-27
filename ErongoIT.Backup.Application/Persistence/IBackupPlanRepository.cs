using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Persistence;

public interface IBackupPlanRepository
{
    Task<IReadOnlyList<BackupPlan>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<BackupPlan?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCustomerAndNameAsync(
        Guid customerId,
        string name,
        Guid? excludePlanId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        BackupPlan backupPlan,
        CancellationToken cancellationToken = default);

    void Remove(BackupPlan backupPlan);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
