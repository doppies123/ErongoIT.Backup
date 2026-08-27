namespace ErongoIT.Backup.Application.Contracts;

public interface IBackupContentStorage
{
    Task<BackupContentStorageResult> StoreAsync(
        Stream data,
        string sha256,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string sha256,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string sha256,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string sha256,
        CancellationToken cancellationToken = default);
}

public sealed record BackupContentStorageResult(
    string StoragePath,
    long BytesStored);
