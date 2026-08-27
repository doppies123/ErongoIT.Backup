using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Domain.Enums;

namespace ErongoIT.Backup.Application.BackupJobs;

public interface IBackupJobService
{
    Task<IReadOnlyList<BackupJob>> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupJob>> GetRestorePointsByDeviceIdAsync(
        Guid deviceId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<BackupJob?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<BackupJob> CreateAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        BackupType type,
        CancellationToken cancellationToken = default);

    Task<bool> StartAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        Guid id,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default);

    Task<bool> FailAsync(
        Guid id,
        string errorMessage,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> RecoverStaleJobsAsync(
        Guid deviceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<int> ClearHistoryAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
