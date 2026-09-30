namespace ErongoIT.Backup.Agent.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Enrolled PCs: per-device secret key (preferred).</summary>
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>Developer fallback only: admin login.</summary>
    public string ApiUsername { get; set; } = string.Empty;

    public string ApiPassword { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid BackupPlanId { get; set; }

    public string AgentVersion { get; set; } = "1.1.0";

    /// <summary>Single folder (older configuration).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>One or more folders to back up.</summary>
    public List<string> SourcePaths { get; set; } = new();

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public int SchedulerIntervalSeconds { get; set; } = 30;

    public int StaleJobTimeoutMinutes { get; set; } = 720;

    public bool UsesDeviceKey =>
        !string.IsNullOrWhiteSpace(DeviceKey);

    /// <summary>All configured folders, de-duplicated.</summary>
    public IReadOnlyList<string> GetSourceFolders()
    {
        var folders = new List<string>();

        foreach (var folder in SourcePaths.Append(SourcePath))
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;

            var full = Path.GetFullPath(folder.Trim());

            if (!folders.Contains(full, StringComparer.OrdinalIgnoreCase))
                folders.Add(full);
        }

        return folders;
    }
}
