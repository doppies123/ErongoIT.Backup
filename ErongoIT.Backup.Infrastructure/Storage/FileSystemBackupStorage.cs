using ErongoIT.Backup.Application.Contracts;

namespace ErongoIT.Backup.Infrastructure.Storage;

public sealed class FileSystemBackupStorage : IBackupStorage
{
    private readonly string _rootPath;

    public FileSystemBackupStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "Backup storage root path is required.",
                nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<BackupStorageResult> StoreAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        Stream data,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(customerId, deviceId, backupJobId);

        if (data is null)
            throw new ArgumentNullException(nameof(data));

        var safeFileName = ValidateFileName(fileName);

        var directory = GetBackupDirectory(
            customerId,
            deviceId,
            backupJobId);

        Directory.CreateDirectory(directory);

        var storagePath = Path.Combine(directory, safeFileName);

        await using var fileStream = new FileStream(
            storagePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            useAsync: true);

        await data.CopyToAsync(fileStream, cancellationToken);

        await fileStream.FlushAsync(cancellationToken);

        var bytesStored = fileStream.Length;

        return new BackupStorageResult(
            Path.GetRelativePath(_rootPath, storagePath),
            bytesStored);
    }

    public Task<Stream?> OpenReadAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(customerId, deviceId, backupJobId);

        var safeFileName = ValidateFileName(fileName);

        var storagePath = GetStoragePath(
            customerId,
            deviceId,
            backupJobId,
            safeFileName);

        if (!File.Exists(storagePath))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> ExistsAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(customerId, deviceId, backupJobId);

        var safeFileName = ValidateFileName(fileName);

        var storagePath = GetStoragePath(
            customerId,
            deviceId,
            backupJobId,
            safeFileName);

        return Task.FromResult(File.Exists(storagePath));
    }

    public Task DeleteAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(customerId, deviceId, backupJobId);

        var safeFileName = ValidateFileName(fileName);

        var storagePath = GetStoragePath(
            customerId,
            deviceId,
            backupJobId,
            safeFileName);

        if (File.Exists(storagePath))
            File.Delete(storagePath);

        return Task.CompletedTask;
    }

    private string GetBackupDirectory(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId)
    {
        return Path.Combine(
            _rootPath,
            customerId.ToString("N"),
            deviceId.ToString("N"),
            backupJobId.ToString("N"));
    }

    private string GetStoragePath(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId,
        string fileName)
    {
        var directory = GetBackupDirectory(
            customerId,
            deviceId,
            backupJobId);

        var path = Path.GetFullPath(
            Path.Combine(directory, fileName));

        var directoryWithSeparator =
            directory.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? directory
                : directory + Path.DirectorySeparatorChar;

        if (!path.StartsWith(
                directoryWithSeparator,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The file name would escape the backup storage directory.",
                nameof(fileName));
        }

        return path;
    }

    private static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException(
                "File name is required.",
                nameof(fileName));

        if (Path.IsPathRooted(fileName))
            throw new ArgumentException(
                "Absolute file paths are not allowed.",
                nameof(fileName));

        if (fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException(
                "Directory paths are not allowed in the file name.",
                nameof(fileName));

        if (fileName is "." or "..")
            throw new ArgumentException(
                "Invalid file name.",
                nameof(fileName));

        return fileName;
    }

    private static void ValidateIds(
        Guid customerId,
        Guid deviceId,
        Guid backupJobId)
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
    }
}
