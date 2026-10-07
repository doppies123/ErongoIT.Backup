using System.Text.Json;

namespace ErongoIT.Backup.Agent.Backup;

/// <summary>
/// What the server currently holds for this PC: for every backed-up file
/// its SHA-256, size and "date modified". A backup compares the disk
/// with this list and only sends what changed, so an unchanged backup
/// costs a folder scan and no uploads.
///
/// Stored in C:\ProgramData\ErongoIT Backup\agent.manifest.json.
/// If it is missing, belongs to another device or is older than
/// <see cref="FullSyncInterval"/>, it is rebuilt from the server.
/// </summary>
public sealed class SyncManifest
{
    public static readonly TimeSpan FullSyncInterval = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public Guid DeviceId { get; set; }

    /// <summary>When the list was last rebuilt from the server.</summary>
    public DateTime FullSyncUtc { get; set; }

    /// <summary>Path ("C:/folder/file.txt") -> entry. Case-insensitive like Windows.</summary>
    public Dictionary<string, Entry> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public sealed record Entry(
        string Sha256,
        long Size,
        long LastWriteUtcTicks);

    public bool NeedsFullSync(Guid deviceId) =>
        DeviceId != deviceId ||
        DateTime.UtcNow - FullSyncUtc > FullSyncInterval;

    public bool IsUnchanged(string path, long size, DateTime lastWriteUtc)
    {
        return Files.TryGetValue(path, out var entry) &&
               entry.Size == size &&
               SameTime(entry.LastWriteUtcTicks, lastWriteUtc.Ticks);
    }

    public void Set(string path, string sha256, long size, DateTime lastWriteUtc)
    {
        Files[path] = new Entry(sha256, size, lastWriteUtc.Ticks);
    }

    public bool TryGetSha256(string path, long size, out string sha256)
    {
        if (Files.TryGetValue(path, out var entry) && entry.Size == size)
        {
            sha256 = entry.Sha256;
            return true;
        }

        sha256 = string.Empty;
        return false;
    }

    public static string FilePath(string directory) =>
        Path.Combine(directory, "agent.manifest.json");

    public static SyncManifest Load(string directory)
    {
        try
        {
            var path = FilePath(directory);

            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);

                var manifest = JsonSerializer.Deserialize<SyncManifest>(stream, JsonOptions);

                if (manifest is not null)
                {
                    // Re-create with the case-insensitive comparer.
                    manifest.Files = new Dictionary<string, Entry>(
                        manifest.Files ?? new Dictionary<string, Entry>(),
                        StringComparer.OrdinalIgnoreCase);

                    return manifest;
                }
            }
        }
        catch
        {
            // Unreadable: rebuild from the server.
        }

        return new SyncManifest();
    }

    /// <summary>Writes the manifest atomically. Never throws.</summary>
    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var path = FilePath(directory);
            var temporary = path + ".tmp";

            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, this, JsonOptions);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Worst case the next backup re-checks more files.
        }
    }

    /// <summary>
    /// Server path for a local file: "C:\Data\a.txt" -> "C:/Data/a.txt".
    /// </summary>
    public static string ToServerPath(string fullPath) =>
        string.Join('/', fullPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries));

    private static bool SameTime(long ticksA, long ticksB) =>
        // The server keeps microseconds (10 ticks).
        Math.Abs(ticksA - ticksB) < 10;
}
