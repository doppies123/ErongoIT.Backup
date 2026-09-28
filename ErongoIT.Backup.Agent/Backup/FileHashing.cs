using System.Security.Cryptography;

namespace ErongoIT.Backup.Agent.Backup;

/// <summary>
/// Local file hashing shared by the background Agent and the Agent GUI.
/// The SHA-256 is compared with what the server already stores so that
/// unchanged files are never uploaded twice.
/// </summary>
public static class FileHashing
{
    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha256 = SHA256.Create();

        var hash = await sha256.ComputeHashAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static FileStream OpenForUpload(
        string filePath)
    {
        return new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
}

/// <summary>A file the client wants the server to record without uploading.</summary>
public sealed record ExistingFileRequest(
    string RelativePath,
    string Sha256,
    long SizeBytes);

/// <summary>Server answer: how many were recorded, and which paths must be uploaded.</summary>
public sealed record RegisterExistingFilesResponse(
    int RegisteredCount,
    long RegisteredBytes,
    IReadOnlyList<string> MissingRelativePaths);
