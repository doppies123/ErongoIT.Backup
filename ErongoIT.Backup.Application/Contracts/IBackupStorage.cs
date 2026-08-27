namespace ErongoIT.Backup.Application.Contracts;

public interface IBackupStorage
{
    Task<BackupStorageResult> StoreAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        Stream data,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName,
        CancellationToken cancellationToken = default);
}

public sealed record BackupStorageResult(
    string StoragePath,
    long BytesStored);
