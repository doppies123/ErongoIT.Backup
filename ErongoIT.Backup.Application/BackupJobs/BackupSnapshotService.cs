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

        // Stream the upload to a temporary file on disk while hashing it.
        // Never buffer the whole file in memory: the VPS has 1 GB RAM and
        // backups can contain multi-GB files.
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"erongoit-upload-{Guid.NewGuid():N}.tmp");

        await using var temporaryStream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        using var hasher = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[1024 * 1024];
        int bytesRead;

        while ((bytesRead = await data.ReadAsync(
                   buffer.AsMemory(0, buffer.Length),
                   cancellationToken)) > 0)
        {
            hasher.AppendData(buffer, 0, bytesRead);

            await temporaryStream.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }

        await temporaryStream.FlushAsync(cancellationToken);

        var sha256Hash = Convert.ToHexString(
                hasher.GetHashAndReset())
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
                // Self-heal: the database knows this content but the
                // physical file is missing (e.g. storage was lost or the
                // database was migrated without the storage folder).
                // We already have the full uploaded bytes and their hash
                // matches, so write the content back to storage.
                temporaryStream.Position = 0;

                await _contentStorage.StoreAsync(
                    temporaryStream,
                    content.Sha256,
                    cancellationToken);
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

    public const int MaxExistingFilesPerRequest = 1000;

    public async Task<RegisterExistingFilesResult> RegisterExistingFilesAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        IReadOnlyCollection<ExistingFileCandidate> files,
        CancellationToken cancellationToken = default)
    {
        if (files is null)
            throw new ArgumentNullException(nameof(files));

        if (files.Count > MaxExistingFilesPerRequest)
            throw new ArgumentException(
                $"A maximum of {MaxExistingFilesPerRequest} files can be checked per request.",
                nameof(files));

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

        var missing = new List<string>();

        if (files.Count == 0)
        {
            return new RegisterExistingFilesResult(
                0,
                0,
                missing);
        }

        var candidates =
            new List<(string OriginalPath, string NormalizedPath, string Sha256, long SizeBytes)>();

        foreach (var file in files)
        {
            if (file is null ||
                string.IsNullOrWhiteSpace(file.RelativePath))
            {
                continue;
            }

            var normalizedPath =
                ValidateRelativePath(file.RelativePath);

            var sha256 =
                (file.Sha256 ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (sha256.Length != 64 ||
                !sha256.All(Uri.IsHexDigit) ||
                file.SizeBytes < 0)
            {
                missing.Add(file.RelativePath);
                continue;
            }

            candidates.Add((
                file.RelativePath,
                normalizedPath,
                sha256,
                file.SizeBytes));
        }

        var contents =
            await _contentRepository.GetBySha256ManyAsync(
                candidates
                    .Select(x => x.Sha256)
                    .Distinct()
                    .ToList(),
                cancellationToken);

        var existingPaths =
            (await _fileRepository.GetByBackupJobIdAsync(
                backupJobId,
                cancellationToken))
            .Select(x => x.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        var physicalExists =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        var registeredCount = 0;
        long registeredBytes = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (existingPaths.Contains(candidate.NormalizedPath))
            {
                // Already recorded in this job (e.g. the client retried).
                registeredCount++;
                registeredBytes += candidate.SizeBytes;
                continue;
            }

            if (!contents.TryGetValue(
                    candidate.Sha256,
                    out var content) ||
                content.SizeBytes != candidate.SizeBytes)
            {
                missing.Add(candidate.OriginalPath);
                continue;
            }

            if (!physicalExists.TryGetValue(
                    candidate.Sha256,
                    out var exists))
            {
                exists = await _contentStorage.ExistsAsync(
                    candidate.Sha256,
                    cancellationToken);

                physicalExists[candidate.Sha256] = exists;
            }

            if (!exists)
            {
                // Database knows the hash but the file is gone:
                // ask the client to upload it so storage self-heals.
                missing.Add(candidate.OriginalPath);
                continue;
            }

            await _fileRepository.AddAsync(
                new BackupFile(
                    backupJobId,
                    content.Id,
                    candidate.NormalizedPath),
                cancellationToken);

            existingPaths.Add(candidate.NormalizedPath);

            registeredCount++;
            registeredBytes += content.SizeBytes;
        }

        await _fileRepository.SaveChangesAsync(
            cancellationToken);

        return new RegisterExistingFilesResult(
            registeredCount,
            registeredBytes,
            missing);
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
        IReadOnlyCollection<Guid> backupFileIds,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (backupJobId == Guid.Empty)
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));

        if (backupFileIds is null)
            throw new ArgumentNullException(nameof(backupFileIds));

        if (backupFileIds.Count == 0)
            throw new ArgumentException(
                "At least one backup file must be selected.",
                nameof(backupFileIds));

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

        var requestedIds = backupFileIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
            throw new ArgumentException(
                "At least one valid backup file must be selected.",
                nameof(backupFileIds));

        var files = await _fileRepository.GetByIdsAsync(
            backupJobId,
            requestedIds,
            cancellationToken);

        if (files.Count != requestedIds.Count)
            throw new InvalidOperationException(
                "One or more selected backup files do not belong to the specified backup job.");

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
