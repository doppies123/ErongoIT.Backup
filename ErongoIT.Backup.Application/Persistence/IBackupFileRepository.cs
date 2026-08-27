using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Persistence;

public interface IBackupFileRepository
{
    Task<IReadOnlyList<BackupFile>> GetByBackupJobIdAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default);

    Task<BackupFile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetFileCountsByBackupJobIdsAsync(
        IReadOnlyCollection<Guid> backupJobIds,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        BackupFile backupFile,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
