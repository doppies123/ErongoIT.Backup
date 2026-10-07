using System.IO.Compression;

namespace ErongoIT.Backup.Agent.Backup;

/// <summary>
/// Compresses files before upload to save bandwidth.
///
/// Only the transfer is compressed: the server decompresses on arrival,
/// so stored content, SHA-256 de-duplication and restores are unchanged.
/// Already-compressed formats are sent as-is, and a file is only sent
/// compressed if that saves at least 10%.
///
/// Uses CompressionLevel.Fastest: roughly 3-5x less CPU than Optimal for
/// only a few percent larger output, so the laptop stays responsive.
/// </summary>
public static class UploadCompression
{
    public const string GzipEncoding = "gzip";

    private const long MinimumSizeBytes = 4 * 1024;
    private const double MaximumRatio = 0.90;

    // Formats that are already compressed: gzip would only waste CPU.
    private static readonly HashSet<string> SkipExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Archives
            ".zip", ".7z", ".rar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".lz", ".lzma", ".cab", ".arj",
            // Office Open XML / OpenDocument (zip containers)
            ".docx", ".xlsx", ".pptx", ".docm", ".xlsm", ".pptm", ".odt", ".ods", ".odp", ".vsdx",
            // Images
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".avif", ".jxl",
            // Audio / video
            ".mp3", ".aac", ".m4a", ".ogg", ".opus", ".flac", ".wma",
            ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".3gp",
            // Packages / disk images / misc compressed
            ".apk", ".jar", ".msi", ".iso", ".dmg", ".epub", ".nupkg", ".vhdx", ".pst.gz"
        };

    public static bool IsWorthTrying(
        string filePath,
        long length)
    {
        if (length < MinimumSizeBytes)
            return false;

        return !SkipExtensions.Contains(
            Path.GetExtension(filePath));
    }

    public static async Task<PreparedUpload> PrepareAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        var originalLength = info.Length;

        if (!IsWorthTrying(filePath, originalLength))
        {
            return new PreparedUpload(
                filePath,
                encoding: null,
                uploadLength: originalLength,
                originalLength: originalLength,
                temporaryFile: false);
        }

        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"erongoit-upload-{Guid.NewGuid():N}.gz");

        try
        {
            await using (var source = FileHashing.OpenForUpload(filePath))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 256 * 1024,
                useAsync: true))
            await using (var gzip = new GZipStream(
                target,
                CompressionLevel.Fastest,
                leaveOpen: false))
            {
                await source.CopyToAsync(
                    gzip,
                    256 * 1024,
                    cancellationToken);
            }

            var compressedLength =
                new FileInfo(temporaryPath).Length;

            if (compressedLength > originalLength * MaximumRatio)
            {
                File.Delete(temporaryPath);

                return new PreparedUpload(
                    filePath,
                    encoding: null,
                    uploadLength: originalLength,
                    originalLength: originalLength,
                    temporaryFile: false);
            }

            return new PreparedUpload(
                temporaryPath,
                GzipEncoding,
                compressedLength,
                originalLength,
                temporaryFile: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            throw;
        }
    }
}

/// <summary>The file to actually send (original or a temporary .gz).</summary>
public sealed class PreparedUpload : IAsyncDisposable, IDisposable
{
    private readonly bool _temporaryFile;

    public PreparedUpload(
        string uploadPath,
        string? encoding,
        long uploadLength,
        long originalLength,
        bool temporaryFile)
    {
        UploadPath = uploadPath;
        Encoding = encoding;
        UploadLength = uploadLength;
        OriginalLength = originalLength;
        _temporaryFile = temporaryFile;
    }

    public string UploadPath { get; }

    /// <summary>"gzip" when compressed, otherwise null.</summary>
    public string? Encoding { get; }

    public long UploadLength { get; }

    public long OriginalLength { get; }

    public bool IsCompressed => Encoding is not null;

    public void Dispose()
    {
        if (!_temporaryFile)
            return;

        try
        {
            if (File.Exists(UploadPath))
                File.Delete(UploadPath);
        }
        catch
        {
            // Temp folder cleanup will get it eventually.
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
