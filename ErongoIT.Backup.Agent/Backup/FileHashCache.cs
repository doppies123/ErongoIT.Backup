using System.Text.Json;

namespace ErongoIT.Backup.Agent.Backup;

/// <summary>
/// Remembers each file's SHA-256 together with its size and last-write
/// time. If neither changed since the last backup, the stored hash is
/// reused and the file is not read again. Entries are re-verified by a
/// full read after <see cref="MaxAge"/> as a safety net.
///
/// Stored per user in %LOCALAPPDATA%\ErongoIT.Backup\{name}.hashcache.json.
/// It is only a cache: deleting it just means the next backup re-hashes.
/// </summary>
public sealed class FileHashCache
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly Dictionary<string, Entry> _entries;
    private readonly object _lock = new();

    public TimeSpan MaxAge { get; }

    public int Hits { get; private set; }

    public int Misses { get; private set; }

    public sealed record Entry(
        long Size,
        long LastWriteUtcTicks,
        string Sha256,
        DateTime HashedAtUtc);

    private FileHashCache(
        string path,
        Dictionary<string, Entry> entries,
        TimeSpan maxAge)
    {
        _path = path;
        _entries = entries;
        MaxAge = maxAge;
    }

    public static FileHashCache Load(
        string name,
        TimeSpan? maxAge = null,
        string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ErongoIT.Backup");

        var path = Path.Combine(
            directory,
            $"{name}.hashcache.json");

        var entries = new Dictionary<string, Entry>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                    File.ReadAllText(path),
                    JsonOptions);

                if (loaded is not null)
                {
                    foreach (var pair in loaded)
                        entries[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable cache: start fresh.
            entries.Clear();
        }

        return new FileHashCache(
            path,
            entries,
            maxAge ?? TimeSpan.FromDays(7));
    }

    public async Task<string> GetSha256Async(
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(fullPath);

        if (!info.Exists)
            throw new FileNotFoundException(
                "File disappeared before it could be hashed.",
                fullPath);

        var size = info.Length;
        var lastWrite = info.LastWriteTimeUtc.Ticks;

        lock (_lock)
        {
            if (_entries.TryGetValue(fullPath, out var entry) &&
                entry.Size == size &&
                entry.LastWriteUtcTicks == lastWrite &&
                DateTime.UtcNow - entry.HashedAtUtc < MaxAge &&
                entry.Sha256.Length == 64)
            {
                Hits++;
                return entry.Sha256;
            }
        }

        var sha256 = await FileHashing.ComputeSha256Async(
            fullPath,
            cancellationToken);

        info.Refresh();

        lock (_lock)
        {
            Misses++;

            // Only cache if the file did not change while we read it.
            if (info.Exists &&
                info.Length == size &&
                info.LastWriteTimeUtc.Ticks == lastWrite)
            {
                _entries[fullPath] = new Entry(
                    size,
                    lastWrite,
                    sha256,
                    DateTime.UtcNow);
            }
            else
            {
                _entries.Remove(fullPath);
            }
        }

        return sha256;
    }

    /// <summary>
    /// Writes the cache to disk, dropping entries for files that no
    /// longer exist. Never throws.
    /// </summary>
    public void Save()
    {
        try
        {
            Dictionary<string, Entry> snapshot;

            lock (_lock)
            {
                foreach (var key in _entries.Keys.ToList())
                {
                    if (!File.Exists(key))
                        _entries.Remove(key);
                }

                snapshot = new Dictionary<string, Entry>(
                    _entries,
                    StringComparer.OrdinalIgnoreCase);
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(_path)!);

            var temporaryPath = _path + ".tmp";

            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(snapshot, JsonOptions));

            File.Move(
                temporaryPath,
                _path,
                overwrite: true);
        }
        catch
        {
            // A cache that cannot be saved only costs a re-hash next time.
        }
    }
}
