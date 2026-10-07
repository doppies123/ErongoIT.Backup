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

    public string AgentVersion { get; set; } = "1.3.1";

    /// <summary>Single folder (older configuration).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>One or more folders to back up.</summary>
    public List<string> SourcePaths { get; set; } = new();

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public int SchedulerIntervalSeconds { get; set; } = 30;

    public int StaleJobTimeoutMinutes { get; set; } = 720;

    // ------------------------------------------------------------
    // Performance: keep the PC responsive while a backup runs.
    // ------------------------------------------------------------

    /// <summary>Run with very low disk I/O and idle CPU priority (Windows background mode).</summary>
    public bool BackgroundMode { get; set; } = true;

    /// <summary>Max disk read speed when the PC is quiet. 0 = unlimited.</summary>
    public int MaxReadMegabytesPerSecond { get; set; } = 50;

    /// <summary>Max disk read speed while the user is busy (CPU above BusyCpuPercent).</summary>
    public int BusyReadMegabytesPerSecond { get; set; } = 5;

    /// <summary>Total CPU % above which the PC counts as busy. 0 = never slow down.</summary>
    public int BusyCpuPercent { get; set; } = 40;

    /// <summary>Scheduled backups are postponed while a laptop is on battery below this %. 0 = always run.</summary>
    public int MinimumBatteryPercent { get; set; } = 30;

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
