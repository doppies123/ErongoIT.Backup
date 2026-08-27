using System.Security.Cryptography;
using ErongoIT.Backup.Application.Contracts;

namespace ErongoIT.Backup.Infrastructure.Storage;

public sealed class FileSystemBackupContentStorage : IBackupContentStorage
{
    private readonly string _rootPath;

    public FileSystemBackupContentStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "Backup storage root path is required.",
                nameof(rootPath));

        _rootPath = Path.GetFullPath(
            Path.Combine(rootPath, "content"));

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<BackupContentStorageResult> StoreAsync(
        Stream data,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        var normalizedHash = ValidateSha256(sha256);

        var storagePath = GetStoragePath(normalizedHash);

        Directory.CreateDirectory(
            Path.GetDirectoryName(storagePath)!);

        if (File.Exists(storagePath))
        {
            var existingLength = new FileInfo(storagePath).Length;

            return new BackupContentStorageResult(
                GetRelativeStoragePath(storagePath),
                existingLength);
        }

        var temporaryPath =
            storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var fileStream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                await data.CopyToAsync(
                    fileStream,
                    cancellationToken);

                await fileStream.FlushAsync(
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var bytesStored = new FileInfo(temporaryPath).Length;

            if (bytesStored < 0)
                throw new IOException(
                    "Unable to determine stored content size.");

            if (File.Exists(storagePath))
            {
                File.Delete(temporaryPath);

                return new BackupContentStorageResult(
                    GetRelativeStoragePath(storagePath),
                    new FileInfo(storagePath).Length);
            }

            File.Move(
                temporaryPath,
                storagePath);

            return new BackupContentStorageResult(
                GetRelativeStoragePath(storagePath),
                bytesStored);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public Task<Stream?> OpenReadAsync(
        string sha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedHash = ValidateSha256(sha256);
        var storagePath = GetStoragePath(normalizedHash);

        if (!File.Exists(storagePath))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> ExistsAsync(
        string sha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedHash = ValidateSha256(sha256);

        return Task.FromResult(
            File.Exists(GetStoragePath(normalizedHash)));
    }

    public Task DeleteAsync(
        string sha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedHash = ValidateSha256(sha256);
        var storagePath = GetStoragePath(normalizedHash);

        if (File.Exists(storagePath))
            File.Delete(storagePath);

        return Task.CompletedTask;
    }

    private string GetStoragePath(string sha256)
    {
        var directory = Path.Combine(
            _rootPath,
            sha256[..2]);

        return Path.Combine(
            directory,
            sha256);
    }

    private string GetRelativeStoragePath(string storagePath)
    {
        return Path.GetRelativePath(
            Path.GetFullPath(
                Directory.GetParent(_rootPath)!.FullName),
            storagePath);
    }

    private static string ValidateSha256(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            throw new ArgumentException(
                "SHA-256 hash is required.",
                nameof(sha256));

        var normalized = sha256.Trim().ToLowerInvariant();

        if (normalized.Length != 64)
            throw new ArgumentException(
                "SHA-256 hash must contain exactly 64 hexadecimal characters.",
                nameof(sha256));

        foreach (var character in normalized)
        {
            if (!Uri.IsHexDigit(character))
                throw new ArgumentException(
                    "SHA-256 hash contains invalid characters.",
                    nameof(sha256));
        }

        return normalized;
    }
}
