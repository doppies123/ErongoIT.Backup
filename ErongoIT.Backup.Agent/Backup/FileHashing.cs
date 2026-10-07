using System.Security.Cryptography;
using ErongoIT.Backup.Agent.Performance;

namespace ErongoIT.Backup.Agent.Backup;

/// <summary>
/// Local file hashing shared by the background Agent and the Agent GUI.
/// The SHA-256 is compared with what the server already stores so that
/// unchanged files are never uploaded twice.
///
/// All reads go through <see cref="IoThrottle.Current"/> when the
/// background Agent has set one, so a backup never floods the disk.
/// </summary>
public static class FileHashing
{
    private const int ReadChunkSize = 256 * 1024;

    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = OpenForRead(filePath);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[ReadChunkSize];

        while (true)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (read == 0)
                break;

            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(
            hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Opens a file for upload (throttled in the background Agent).</summary>
    public static Stream OpenForUpload(
        string filePath)
    {
        return OpenForRead(filePath);
    }

    /// <summary>
    /// Opens a file for sequential reading. Files that other programs have
    /// open (Outlook, databases) can still be read.
    /// </summary>
    public static Stream OpenForRead(
        string filePath)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: ReadChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var throttle = IoThrottle.Current;

        return throttle is null
            ? stream
            : new ThrottledReadStream(stream, throttle);
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
