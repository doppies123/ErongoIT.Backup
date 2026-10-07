namespace ErongoIT.Backup.Domain.Entities;

/// <summary>
/// One version of one file on one device (CrashPlan-style history).
///
/// A version is valid from <see cref="ValidFromUtc"/> until
/// <see cref="ValidToUtc"/>. ValidToUtc is null while it is the current
/// version. When the file changes, the current version is closed and a
/// new one is opened; when the file is deleted, the current version is
/// closed and no new one follows.
///
/// "What did the PC look like at time T" is therefore:
///   ValidFromUtc &lt;= T AND (ValidToUtc IS NULL OR ValidToUtc &gt; T)
///
/// Paths are the full original path with '/' separators, for example
/// "C:/Users/Raymond/Documents/report.docx". <see cref="Folder"/> is the
/// parent path ("C:/Users/Raymond/Documents") and <see cref="Name"/> the
/// file name, so a folder can be listed without parsing every path.
/// </summary>
public sealed class FileVersion
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid CustomerId { get; private set; }

    public Guid DeviceId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public string Folder { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid BackupContentId { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>The file's "Date modified" on the PC.</summary>
    public DateTime? LastWriteUtc { get; private set; }

    /// <summary>The backup job that recorded this version.</summary>
    public Guid? BackupJobId { get; private set; }

    public DateTime ValidFromUtc { get; private set; }

    public DateTime? ValidToUtc { get; private set; }

    public bool IsCurrent => ValidToUtc is null;

    private FileVersion()
    {
    }

    public FileVersion(
        Guid customerId,
        Guid deviceId,
        string path,
        Guid backupContentId,
        long sizeBytes,
        DateTime? lastWriteUtc,
        Guid? backupJobId,
        DateTime validFromUtc)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID is required.", nameof(customerId));

        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device ID is required.", nameof(deviceId));

        if (backupContentId == Guid.Empty)
            throw new ArgumentException("Backup content ID is required.", nameof(backupContentId));

        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        var normalized = NormalizePath(path);
        var slash = normalized.LastIndexOf('/');

        CustomerId = customerId;
        DeviceId = deviceId;
        Path = normalized;
        Folder = slash < 0 ? string.Empty : normalized[..slash];
        Name = slash < 0 ? normalized : normalized[(slash + 1)..];
        BackupContentId = backupContentId;
        SizeBytes = sizeBytes;
        LastWriteUtc = lastWriteUtc is null
            ? null
            : DateTime.SpecifyKind(lastWriteUtc.Value, DateTimeKind.Utc);
        BackupJobId = backupJobId;
        ValidFromUtc = DateTime.SpecifyKind(validFromUtc, DateTimeKind.Utc);
    }

    /// <summary>Marks this version as replaced or deleted at the given time.</summary>
    public void Close(DateTime atUtc)
    {
        if (ValidToUtc is not null)
            return;

        ValidToUtc = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
    }

    /// <summary>
    /// Normalizes a client path: '\' becomes '/', no empty, "." or ".."
    /// segments, no leading or trailing slash. Drive letters ("C:") are
    /// kept as the first segment so the original location is known.
    /// </summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var segments = path
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (segments.Length == 0)
            throw new ArgumentException("Path is invalid.", nameof(path));

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
                throw new ArgumentException("Path traversal is not allowed.", nameof(path));
        }

        var normalized = string.Join('/', segments);

        if (normalized.Length > 2000)
            throw new ArgumentException("Path is too long.", nameof(path));

        return normalized;
    }

    /// <summary>Normalizes a folder path; empty means "the top" (all drives).</summary>
    public static string NormalizeFolder(string? folder)
    {
        return string.IsNullOrWhiteSpace(folder) ||
               folder.Trim().Replace('\\', '/').Trim('/').Length == 0
            ? string.Empty
            : NormalizePath(folder);
    }
}
