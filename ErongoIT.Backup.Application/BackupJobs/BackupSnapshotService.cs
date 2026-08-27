using System.Security.Cryptography;
using ErongoIT.Backup.Application.Contracts;
using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.BackupJobs;

public sealed class BackupSnapshotService : IBackupSnapshotService
{
    private readonly IBackupContentRepository _contentRepository;
    private readonly IBackupFileRepository _fileRepository;
    private readonly IBackupContentStorage _contentStorage;
    private readonly IBackupJobRepository _jobRepository;

    public BackupSnapshotService(
        IBackupContentRepository contentRepository,
        IBackupFileRepository fileRepository,
        IBackupContentStorage contentStorage,
        IBackupJobRepository jobRepository)
    {
        _contentRepository = contentRepository;
        _fileRepository = fileRepository;
        _contentStorage = contentStorage;
        _jobRepository = jobRepository;
    }

    public async Task<BackupFile> StoreFileAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string relativePath,
        Stream data,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException(
                "Customer ID is required.",
                nameof(customerId));

        if (deviceId == Guid.Empty)
            throw new ArgumentException(
                "Device ID is required.",
                nameof(deviceId));

        if (backupJobId == Guid.Empty)
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException(
                "Relative path is required.",
                nameof(relativePath));

        if (data is null)
            throw new ArgumentNullException(nameof(data));

        if (!data.CanRead)
            throw new ArgumentException(
                "The supplied stream must be readable.",
                nameof(data));

        var job = await _jobRepository.GetByIdAsync(
            backupJobId,
            cancellationToken);

        if (job is null)
            throw new InvalidOperationException(
                "Backup job was not found.");

        if (job.CustomerId != customerId)
            throw new InvalidOperationException(
                "Backup job does not belong to the specified customer.");

        if (job.DeviceId != deviceId)
            throw new InvalidOperationException(
                "Backup job does not belong to the specified device.");

        if (job.Status != "Running")
            throw new InvalidOperationException(
                "Files can only be added to a running backup job.");

        var normalizedRelativePath =
            ValidateRelativePath(relativePath);

        using var sha256 = SHA256.Create();

        await using var temporaryStream = new MemoryStream();

        await data.CopyToAsync(
            temporaryStream,
            cancellationToken);

        temporaryStream.Position = 0;

        var hashBytes = await sha256.ComputeHashAsync(
            temporaryStream,
            cancellationToken);

        var sha256Hash = Convert.ToHexString(hashBytes)
            .ToLowerInvariant();

        var sizeBytes = temporaryStream.Length;

        var content = await _contentRepository.GetBySha256Async(
            sha256Hash,
            cancellationToken);

        if (content is null)
        {
            temporaryStream.Position = 0;

            var storageResult =
                await _contentStorage.StoreAsync(
                    temporaryStream,
                    sha256Hash,
                    cancellationToken);

            content = new BackupContent(
                sha256Hash,
                sizeBytes,
                storageResult.StoragePath);

            await _contentRepository.AddAsync(
                content,
                cancellationToken);
        }
        else
        {
            if (content.SizeBytes != sizeBytes)
            {
                throw new InvalidOperationException(
                    "Existing backup content has the same SHA-256 hash but a different size.");
            }

            if (!await _contentStorage.ExistsAsync(
                    content.Sha256,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "Backup content exists in the database but the physical content is missing.");
            }
        }

        var backupFile = new BackupFile(
            backupJobId,
            content.Id,
            normalizedRelativePath);

        await _fileRepository.AddAsync(
            backupFile,
            cancellationToken);

        await _contentRepository.SaveChangesAsync(
            cancellationToken);

        return backupFile;
    }

    public async Task<IReadOnlyList<BackupFile>> GetFilesAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        if (backupJobId == Guid.Empty)
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));

        var job = await _jobRepository.GetByIdAsync(
            backupJobId,
            cancellationToken);

        if (job is null)
            throw new InvalidOperationException(
                "Backup job was not found.");

        if (job.Status != "Completed")
            throw new InvalidOperationException(
                "Only completed backup jobs can be used as restore points.");

        return await _fileRepository.GetByBackupJobIdAsync(
            backupJobId,
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetFileCountsByBackupJobIdsAsync(
        IReadOnlyCollection<Guid> backupJobIds,
        CancellationToken cancellationToken = default)
    {
        if (backupJobIds is null)
            throw new ArgumentNullException(nameof(backupJobIds));

        return _fileRepository.GetFileCountsByBackupJobIdsAsync(
            backupJobIds,
            cancellationToken);
    }

    public async Task<BackupRestoreFile?> OpenFileAsync(
        Guid backupJobId,
        Guid backupFileId,
        CancellationToken cancellationToken = default)
    {
        if (backupJobId == Guid.Empty)
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));

        if (backupFileId == Guid.Empty)
            throw new ArgumentException(
                "Backup file ID is required.",
                nameof(backupFileId));

        var job = await _jobRepository.GetByIdAsync(
            backupJobId,
            cancellationToken);

        if (job is null)
            throw new InvalidOperationException(
                "Backup job was not found.");

        if (job.Status != "Completed")
            throw new InvalidOperationException(
                "Only completed backup jobs can be restored.");

        var backupFile = await _fileRepository.GetByIdAsync(
            backupFileId,
            cancellationToken);

        if (backupFile is null)
            return null;

        if (backupFile.BackupJobId != backupJobId)
            throw new InvalidOperationException(
                "Backup file does not belong to the specified backup job.");

        var content = await _contentRepository.GetByIdAsync(
            backupFile.BackupContentId,
            cancellationToken);

        if (content is null)
            throw new InvalidOperationException(
                "Backup content was not found.");

        var stream = await _contentStorage.OpenReadAsync(
            content.Sha256,
            cancellationToken);

        if (stream is null)
            throw new InvalidOperationException(
                "Backup content exists in the database but the physical content is missing.");

        var fileName = Path.GetFileName(
            backupFile.RelativePath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            await stream.DisposeAsync();

            throw new InvalidOperationException(
                "The backup file has an invalid file name.");
        }

        return new BackupRestoreFile(
            stream,
            fileName,
            content.SizeBytes);
    }

    public async Task<BackupRestoreResult> RestoreAsync(
        Guid backupJobId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (backupJobId == Guid.Empty)
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException(
                "Destination path is required.",
                nameof(destinationPath));

        var job = await _jobRepository.GetByIdAsync(
            backupJobId,
            cancellationToken);

        if (job is null)
            throw new InvalidOperationException(
                "Backup job was not found.");

        if (job.Status != "Completed")
            throw new InvalidOperationException(
                "Only completed backup jobs can be restored.");

        var files = await _fileRepository.GetByBackupJobIdAsync(
            backupJobId,
            cancellationToken);

        var destinationRoot = Path.GetFullPath(
            destinationPath);

        Directory.CreateDirectory(destinationRoot);

        var filesRestored = 0;
        long bytesRestored = 0;

        foreach (var backupFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = ValidateRelativePath(
                backupFile.RelativePath);

            var restorePath = GetSafeRestorePath(
                destinationRoot,
                relativePath);

            var content = await _contentRepository.GetByIdAsync(
                backupFile.BackupContentId,
                cancellationToken);

            if (content is null)
                throw new InvalidOperationException(
                    $"Backup content '{backupFile.BackupContentId}' was not found.");

            var sourceStream = await _contentStorage.OpenReadAsync(
                content.Sha256,
                cancellationToken);

            if (sourceStream is null)
                throw new InvalidOperationException(
                    $"Physical backup content '{content.Sha256}' is missing.");

            try
            {
                var parentDirectory =
                    Path.GetDirectoryName(restorePath);

                if (!string.IsNullOrWhiteSpace(parentDirectory))
                    Directory.CreateDirectory(parentDirectory);

                await using var destinationStream =
                    new FileStream(
                        restorePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1024 * 1024,
                        useAsync: true);

                await sourceStream.CopyToAsync(
                    destinationStream,
                    cancellationToken);

                await destinationStream.FlushAsync(
                    cancellationToken);

                filesRestored++;
                bytesRestored += content.SizeBytes;
            }
            finally
            {
                await sourceStream.DisposeAsync();
            }
        }

        return new BackupRestoreResult(
            backupJobId,
            destinationRoot,
            filesRestored,
            bytesRestored);
    }

    private static string ValidateRelativePath(
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException(
                "Relative path is required.",
                nameof(relativePath));

        var normalized = relativePath
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
            throw new ArgumentException(
                "Absolute paths are not allowed.",
                nameof(relativePath));

        var segments = normalized.Split(
            new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            },
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            throw new ArgumentException(
                "Relative path is invalid.",
                nameof(relativePath));

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
                throw new ArgumentException(
                    "Path traversal is not allowed.",
                    nameof(relativePath));
        }

        return string.Join(
            Path.DirectorySeparatorChar,
            segments);
    }

    private static string GetSafeRestorePath(
        string destinationRoot,
        string relativePath)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(
                destinationRoot,
                relativePath));

        var rootWithSeparator =
            destinationRoot.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? destinationRoot
                : destinationRoot +
                  Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                rootWithSeparator,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The backup file would escape the restore destination.");
        }

        return fullPath;
    }
}
