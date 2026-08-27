using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Persistence;

public interface IBackupContentRepository
{
    Task<BackupContent?> GetBySha256Async(
        string sha256,
        CancellationToken cancellationToken = default);

    Task<BackupContent?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        BackupContent content,
        CancellationToken cancellationToken = default);

    Task<bool> HasReferencesAsync(
        Guid contentId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        BackupContent content,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
