namespace ErongoIT.Backup.Agent.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string ApiBaseUrl { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid BackupPlanId { get; set; }

    public string AgentVersion { get; set; } = "1.0.0";

    public string SourcePath { get; set; } = string.Empty;

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public int SchedulerIntervalSeconds { get; set; } = 30;

    public int StaleJobTimeoutMinutes { get; set; } = 30;
}
