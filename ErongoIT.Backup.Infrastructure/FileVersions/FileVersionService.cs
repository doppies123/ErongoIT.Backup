using System.Security.Cryptography;
using ErongoIT.Backup.Application.Contracts;
using ErongoIT.Backup.Application.FileVersions;
using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.FileVersions;

public sealed class FileVersionService : IFileVersionService
{
    private readonly BackupDbContext _db;
    private readonly IBackupContentStorage _contentStorage;

    public FileVersionService(
        BackupDbContext db,
        IBackupContentStorage contentStorage)
    {
        _db = db;
        _contentStorage = contentStorage;
    }

    // ------------------------------------------------------------------
    // Content
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<string>> GetMissingContentAsync(
        IReadOnlyCollection<ContentReference> contents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);

        if (contents.Count > IFileVersionService.MaxItemsPerRequest)
            throw new ArgumentException(
                $"A maximum of {IFileVersionService.MaxItemsPerRequest} items can be checked per request.");

        var wanted = contents
            .Where(c => c is not null)
            .Select(c => new { Sha = NormalizeSha(c.Sha256), c.SizeBytes })
            .GroupBy(c => c.Sha)
            .Select(g => g.First())
            .ToList();

        var shas = wanted.Select(w => w.Sha).ToList();

        var known = await _db.BackupContents
            .AsNoTracking()
            .Where(c => shas.Contains(c.Sha256))
            .Select(c => new { c.Sha256, c.SizeBytes })
            .ToDictionaryAsync(c => c.Sha256, c => c.SizeBytes, cancellationToken);

        var missing = new List<string>();

        foreach (var item in wanted)
        {
            if (!known.TryGetValue(item.Sha, out var size) ||
                size != item.SizeBytes ||
                !await _contentStorage.ExistsAsync(item.Sha, cancellationToken))
            {
                missing.Add(item.Sha);
            }
        }

        return missing;
    }

    public async Task<StoredContent> StoreContentAsync(
        Stream data,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var expected = NormalizeSha(expectedSha256);

        // Stream to a temporary file while hashing: never buffer whole
        // files in memory on the 1 GB VPS.
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"erongoit-content-{Guid.NewGuid():N}.tmp");

        await using var temporaryStream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[1024 * 1024];
        int read;

        while ((read = await data.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            await temporaryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await temporaryStream.FlushAsync(cancellationToken);

        var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var size = temporaryStream.Length;

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Uploaded content does not match its SHA-256 (expected {expected}, received {actual}). " +
                "The file probably changed while it was being uploaded.");

        var content = await _db.BackupContents
            .FirstOrDefaultAsync(c => c.Sha256 == actual, cancellationToken);

        if (content is not null)
        {
            if (content.SizeBytes != size)
                throw new InvalidOperationException(
                    "Existing backup content has the same SHA-256 hash but a different size.");

            if (!await _contentStorage.ExistsAsync(actual, cancellationToken))
            {
                // Self-heal: the database knows the content but the file is gone.
                temporaryStream.Position = 0;
                await _contentStorage.StoreAsync(temporaryStream, actual, cancellationToken);
            }

            return new StoredContent(actual, size, AlreadyExisted: true);
        }

        temporaryStream.Position = 0;

        var stored = await _contentStorage.StoreAsync(
            temporaryStream,
            actual,
            cancellationToken);

        _db.BackupContents.Add(new BackupContent(actual, size, stored.StoragePath));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another upload of the same content won the race: fine.
            _db.ChangeTracker.Clear();
        }

        return new StoredContent(actual, size, AlreadyExisted: false);
    }

    // ------------------------------------------------------------------
    // Changes from the agent
    // ------------------------------------------------------------------

    public async Task<ApplyChangesResult> ApplyChangesAsync(
        Guid deviceId,
        Guid? backupJobId,
        IReadOnlyList<FileChange> changed,
        IReadOnlyList<string> deleted,
        CancellationToken cancellationToken = default)
    {
        changed ??= Array.Empty<FileChange>();
        deleted ??= Array.Empty<string>();

        if (changed.Count + deleted.Count > IFileVersionService.MaxItemsPerRequest)
            throw new ArgumentException(
                $"A maximum of {IFileVersionService.MaxItemsPerRequest} changes can be sent per request.");

        var device = await _db.Devices
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Id, d.CustomerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Device was not found.");

        if (backupJobId is Guid jobId)
        {
            var jobOk = await _db.BackupJobs
                .AnyAsync(j => j.Id == jobId && j.DeviceId == deviceId, cancellationToken);

            if (!jobOk)
                throw new InvalidOperationException("Backup job does not belong to this device.");
        }

        // Normalise input; the last entry for a path wins.
        var changes = new Dictionary<string, FileChange>(StringComparer.Ordinal);

        foreach (var change in changed)
        {
            if (change is null)
                continue;

            var path = FileVersion.NormalizePath(change.Path);

            if (change.SizeBytes < 0)
                throw new ArgumentException($"Invalid size for '{path}'.");

            changes[path] = change with
            {
                Path = path,
                Sha256 = NormalizeSha(change.Sha256)
            };
        }

        var deletions = deleted
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(FileVersion.NormalizePath)
            .Where(p => !changes.ContainsKey(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var allPaths = changes.Keys.Concat(deletions).ToList();

        if (allPaths.Count == 0)
            return new ApplyChangesResult(0, 0, 0, 0, Array.Empty<string>());

        var shas = changes.Values.Select(c => c.Sha256).Distinct().ToList();

        var contents = await _db.BackupContents
            .AsNoTracking()
            .Where(c => shas.Contains(c.Sha256))
            .Select(c => new { c.Id, c.Sha256, c.SizeBytes })
            .ToDictionaryAsync(c => c.Sha256, cancellationToken);

        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var current = await _db.FileVersions
            .Where(v => v.DeviceId == deviceId &&
                        v.ValidToUtc == null &&
                        allPaths.Contains(v.Path))
            .ToDictionaryAsync(v => v.Path, StringComparer.Ordinal, cancellationToken);

        var missing = new List<string>();
        var toAdd = new List<FileVersion>();
        int added = 0, updated = 0, unchanged = 0, removed = 0;

        foreach (var change in changes.Values)
        {
            if (!contents.TryGetValue(change.Sha256, out var content) ||
                content.SizeBytes != change.SizeBytes)
            {
                missing.Add(change.Path);
                continue;
            }

            var lastWrite = change.LastWriteUtc is null
                ? (DateTime?)null
                : DateTime.SpecifyKind(change.LastWriteUtc.Value, DateTimeKind.Utc);

            if (current.TryGetValue(change.Path, out var existing))
            {
                if (existing.BackupContentId == content.Id &&
                    SameTime(existing.LastWriteUtc, lastWrite))
                {
                    unchanged++;
                    continue;
                }

                existing.Close(now);
                updated++;
            }
            else
            {
                added++;
            }

            toAdd.Add(new FileVersion(
                device.CustomerId,
                deviceId,
                change.Path,
                content.Id,
                content.SizeBytes,
                lastWrite,
                backupJobId,
                now));
        }

        foreach (var path in deletions)
        {
            if (current.TryGetValue(path, out var existing))
            {
                existing.Close(now);
                removed++;
            }
        }

        // Close old versions first, then add new ones: only one version per
        // file may be "current" (unique index on open versions).
        await _db.SaveChangesAsync(cancellationToken);

        if (toAdd.Count > 0)
        {
            _db.FileVersions.AddRange(toAdd);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new ApplyChangesResult(added, updated, unchanged, removed, missing);
    }

    public async Task<CurrentStatePage> GetCurrentStateAsync(
        Guid deviceId,
        string? afterPath,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 10000);

        var query = _db.FileVersions
            .AsNoTracking()
            .Where(v => v.DeviceId == deviceId && v.ValidToUtc == null);

        if (!string.IsNullOrEmpty(afterPath))
            query = query.Where(v => string.Compare(v.Path, afterPath) > 0);

        var rows = await query
            .Join(
                _db.BackupContents,
                v => v.BackupContentId,
                c => c.Id,
                (v, c) => new { v.Path, c.Sha256, v.SizeBytes, v.LastWriteUtc })
            .OrderBy(x => x.Path)
            .Take(take)
            .ToListAsync(cancellationToken);

        // Keep the database's order: the next page continues after the
        // last path using the same (database) comparison.
        var files = rows
            .Select(x => new CurrentFile(x.Path, x.Sha256, x.SizeBytes, Utc(x.LastWriteUtc)))
            .ToList();

        var next = files.Count == take ? files[^1].Path : null;

        return new CurrentStatePage(files, next);
    }

    // ------------------------------------------------------------------
    // Restore: browse, resolve, search, download
    // ------------------------------------------------------------------

    public async Task<FolderListing> BrowseAsync(
        Guid deviceId,
        string? folder,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        var root = FileVersion.NormalizeFolder(folder);
        var asOf = AsOf(asOfUtc);

        var query = _db.FileVersions
            .AsNoTracking()
            .Where(v => v.DeviceId == deviceId);

        if (root.Length > 0)
        {
            var prefix = root + "/";
            query = query.Where(v => v.Folder == root || v.Folder.StartsWith(prefix));
        }

        var rows = await LoadVisibleAsync(query, asOf, includeDeleted, cancellationToken);

        var files = new List<VersionItem>();
        var children = new Dictionary<string, ChildFolder>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (row.Folder == root)
            {
                files.Add(row.ToItem());
                continue;
            }

            // First folder segment below the root.
            var rest = root.Length == 0 ? row.Folder : row.Folder[(root.Length + 1)..];
            var slash = rest.IndexOf('/');
            var childName = slash < 0 ? rest : rest[..slash];

            if (!children.TryGetValue(childName, out var child))
            {
                child = new ChildFolder(root.Length == 0 ? childName : $"{root}/{childName}");
                children[childName] = child;
            }

            child.Add(row);
        }

        var folders = children
            .Select(c => c.Value.ToItem(c.Key))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        files = files
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FolderListing(root, asOf, folders, files);
    }

    public async Task<IReadOnlyList<VersionItem>> ResolveAsync(
        Guid deviceId,
        IReadOnlyList<string> paths,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        if (paths is null || paths.Count == 0)
            return Array.Empty<VersionItem>();

        if (paths.Count > 500)
            throw new ArgumentException("A maximum of 500 files and folders can be selected at once.");

        var asOf = AsOf(asOfUtc);
        var result = new Dictionary<Guid, VersionItem>();

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var path = FileVersion.NormalizeFolder(raw);

            var query = _db.FileVersions
                .AsNoTracking()
                .Where(v => v.DeviceId == deviceId);

            if (path.Length > 0)
            {
                var prefix = path + "/";
                query = query.Where(v =>
                    v.Path == path ||
                    v.Folder == path ||
                    v.Folder.StartsWith(prefix));
            }

            foreach (var row in await LoadVisibleAsync(query, asOf, includeDeleted, cancellationToken))
                result[row.Id] = row.ToItem();
        }

        return result.Values
            .OrderBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<VersionItem>> SearchAsync(
        Guid deviceId,
        string query,
        DateTime? asOfUtc,
        bool includeDeleted,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<VersionItem>();

        take = Math.Clamp(take, 1, 2000);

        var pattern = "%" + query.Trim()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_") + "%";

        var asOf = AsOf(asOfUtc);

        var source = _db.FileVersions
            .AsNoTracking()
            .Where(v => v.DeviceId == deviceId && EF.Functions.ILike(v.Name, pattern));

        var rows = await LoadVisibleAsync(source, asOf, includeDeleted, cancellationToken);

        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(r => r.ToItem())
            .ToList();
    }

    public async Task<VersionContent?> OpenVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await _db.FileVersions
            .AsNoTracking()
            .Where(v => v.Id == versionId)
            .Join(
                _db.BackupContents,
                v => v.BackupContentId,
                c => c.Id,
                (v, c) => new { v.DeviceId, v.Name, c.Sha256, c.SizeBytes })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
            return null;

        var stream = await _contentStorage.OpenReadAsync(version.Sha256, cancellationToken)
            ?? throw new InvalidOperationException(
                "The backup content exists in the database but the stored file is missing.");

        return new VersionContent(version.DeviceId, version.Name, version.SizeBytes, stream);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Loads the versions visible at <paramref name="asOf"/>: the current
    /// version of every file that existed then, plus (optionally) the last
    /// version of files that had been deleted by then.
    /// </summary>
    private async Task<List<VersionRow>> LoadVisibleAsync(
        IQueryable<FileVersion> query,
        DateTime asOf,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        query = includeDeleted
            ? query.Where(v => v.ValidFromUtc <= asOf)
            : query.Where(v => v.ValidFromUtc <= asOf &&
                               (v.ValidToUtc == null || v.ValidToUtc > asOf));

        var rows = await query
            .Select(v => new VersionRow
            {
                Id = v.Id,
                Path = v.Path,
                Folder = v.Folder,
                Name = v.Name,
                SizeBytes = v.SizeBytes,
                LastWriteUtc = v.LastWriteUtc,
                ValidFromUtc = v.ValidFromUtc,
                ValidToUtc = v.ValidToUtc
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            row.Deleted = row.ValidToUtc is not null && row.ValidToUtc <= asOf;

        if (!includeDeleted)
            return rows;

        // Several versions of one path: keep the newest as of the time.
        return rows
            .GroupBy(r => r.Path, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.ValidFromUtc).First())
            .ToList();
    }

    private static DateTime AsOf(DateTime? asOfUtc)
    {
        if (asOfUtc is null)
            return DateTime.UtcNow;

        var value = asOfUtc.Value.Kind == DateTimeKind.Local
            ? asOfUtc.Value.ToUniversalTime()
            : DateTime.SpecifyKind(asOfUtc.Value, DateTimeKind.Utc);

        return value > DateTime.UtcNow ? DateTime.UtcNow : value;
    }

    private static string NormalizeSha(string? sha256)
    {
        var sha = (sha256 ?? string.Empty).Trim().ToLowerInvariant();

        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new ArgumentException($"Invalid SHA-256 '{sha256}'.");

        return sha;
    }

    private static bool SameTime(DateTime? a, DateTime? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        // Postgres stores microseconds; .NET ticks are 100 ns.
        return Math.Abs((a.Value - b.Value).Ticks) < 10;
    }

    private sealed class VersionRow
    {
        public Guid Id { get; init; }
        public string Path { get; init; } = string.Empty;
        public string Folder { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public DateTime? LastWriteUtc { get; init; }
        public DateTime ValidFromUtc { get; init; }
        public DateTime? ValidToUtc { get; init; }
        public bool Deleted { get; set; }

        public VersionItem ToItem() =>
            new(Id, Name, Path, SizeBytes, Utc(LastWriteUtc), Utc(ValidFromUtc)!.Value, Deleted);
    }

    private sealed class ChildFolder
    {
        private readonly string _path;
        private readonly HashSet<string> _subfolders = new(StringComparer.Ordinal);

        private int _files;
        private int _deletedFiles;
        private long _size;
        private DateTime? _lastWrite;

        public ChildFolder(string path) => _path = path;

        public void Add(VersionRow row)
        {
            _files++;
            _size += row.SizeBytes;

            if (row.Deleted)
                _deletedFiles++;

            if (row.LastWriteUtc is not null && (_lastWrite is null || row.LastWriteUtc > _lastWrite))
                _lastWrite = row.LastWriteUtc;

            // Every folder level between this child and the file.
            var folder = row.Folder;

            while (folder.Length > _path.Length)
            {
                if (!_subfolders.Add(folder))
                    break;

                var slash = folder.LastIndexOf('/');

                if (slash <= 0)
                    break;

                folder = folder[..slash];
            }
        }

        public FolderItem ToItem(string name) =>
            new(name, _path, _files, _subfolders.Count, _size, Utc(_lastWrite),
                Deleted: _files > 0 && _deletedFiles == _files);
    }

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
