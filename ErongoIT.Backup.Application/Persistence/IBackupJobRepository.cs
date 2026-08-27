using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Persistence;

public interface IBackupJobRepository
{
    Task<IReadOnlyList<BackupJob>> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<BackupJob?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupJob>> GetActiveJobsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteHistoryByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        BackupJob backupJob,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
