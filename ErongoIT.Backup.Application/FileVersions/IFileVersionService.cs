namespace ErongoIT.Backup.Application.FileVersions;

/// <summary>
/// CrashPlan-style file history per device: the agent sends only what
/// changed, and restores can browse the PC's folders "as of" any time.
/// </summary>
public interface IFileVersionService
{
    public const int MaxItemsPerRequest = 1000;

    /// <summary>Which of these contents the server does not have yet (by SHA-256 and size).</summary>
    Task<IReadOnlyList<string>> GetMissingContentAsync(
        IReadOnlyCollection<ContentReference> contents,
        CancellationToken cancellationToken = default);

    /// <summary>Stores uploaded content; the bytes must match the expected SHA-256.</summary>
    Task<StoredContent> StoreContentAsync(
        Stream data,
        string expectedSha256,
        CancellationToken cancellationToken = default);

    /// <summary>Records changed and deleted files for a device.</summary>
    Task<ApplyChangesResult> ApplyChangesAsync(
        Guid deviceId,
        Guid? backupJobId,
        IReadOnlyList<FileChange> changed,
        IReadOnlyList<string> deleted,
        CancellationToken cancellationToken = default);

    /// <summary>The device's current files, in pages (agent re-sync).</summary>
    Task<CurrentStatePage> GetCurrentStateAsync(
        Guid deviceId,
        string? afterPath,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Folders and files directly inside a folder, as of a time.</summary>
    Task<FolderListing> BrowseAsync(
        Guid deviceId,
        string? folder,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default);

    /// <summary>Every file version inside the selected files and folders, as of a time.</summary>
    Task<IReadOnlyList<VersionItem>> ResolveAsync(
        Guid deviceId,
        IReadOnlyList<string> paths,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default);

    /// <summary>Files whose name contains the text, as of a time.</summary>
    Task<IReadOnlyList<VersionItem>> SearchAsync(
        Guid deviceId,
        string query,
        DateTime? asOfUtc,
        bool includeDeleted,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Opens one version for download. Null when not found.</summary>
    Task<VersionContent?> OpenVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);
}

public sealed record ContentReference(
    string Sha256,
    long SizeBytes);

public sealed record StoredContent(
    string Sha256,
    long SizeBytes,
    bool AlreadyExisted);

public sealed record FileChange(
    string Path,
    string Sha256,
    long SizeBytes,
    DateTime? LastWriteUtc);

public sealed record ApplyChangesResult(
    int Added,
    int Changed,
    int Unchanged,
    int Deleted,
    IReadOnlyList<string> MissingContentPaths);

public sealed record CurrentFile(
    string Path,
    string Sha256,
    long SizeBytes,
    DateTime? LastWriteUtc);

public sealed record CurrentStatePage(
    IReadOnlyList<CurrentFile> Files,
    string? NextAfterPath);

public sealed record FolderItem(
    string Name,
    string Path,
    int FileCount,
    int FolderCount,
    long SizeBytes,
    DateTime? LastWriteUtc,
    bool Deleted);

public sealed record VersionItem(
    Guid VersionId,
    string Name,
    string Path,
    long SizeBytes,
    DateTime? LastWriteUtc,
    DateTime BackedUpUtc,
    bool Deleted);

public sealed record FolderListing(
    string Folder,
    DateTime AsOfUtc,
    IReadOnlyList<FolderItem> Folders,
    IReadOnlyList<VersionItem> Files);

public sealed record VersionContent(
    Guid DeviceId,
    string FileName,
    long SizeBytes,
    Stream Content);
