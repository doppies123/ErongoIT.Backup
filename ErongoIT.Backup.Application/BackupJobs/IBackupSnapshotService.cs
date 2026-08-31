using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.BackupJobs;

public interface IBackupSnapshotService
{
    Task<BackupFile> StoreFileAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string relativePath,
        Stream data,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupFile>> GetFilesAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetFileCountsByBackupJobIdsAsync(
        IReadOnlyCollection<Guid> backupJobIds,
        CancellationToken cancellationToken = default);

    Task<BackupRestoreFile?> OpenFileAsync(
        Guid backupJobId,
        Guid backupFileId,
        CancellationToken cancellationToken = default);

    Task<BackupRestoreResult> RestoreAsync(
        Guid backupJobId,
        IReadOnlyCollection<Guid> backupFileIds,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed record BackupRestoreFile(
    Stream Content,
    string FileName,
    long SizeBytes);

public sealed record BackupRestoreResult(
    Guid BackupJobId,
    string DestinationPath,
    int FilesRestored,
    long BytesRestored);
