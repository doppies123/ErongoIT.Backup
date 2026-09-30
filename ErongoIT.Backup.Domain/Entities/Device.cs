using System.Text.Json.Serialization;

namespace ErongoIT.Backup.Domain.Entities;

public sealed class Device
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid CustomerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Hostname { get; private set; }

    public string? OperatingSystem { get; private set; }

    public string? AgentVersion { get; private set; }

    public DateTime? LastSeenAtUtc { get; private set; }

    public bool IsActive { get; private set; } = true;

    public Guid? AssignedBackupPlanId { get; private set; }

    /// <summary>
    /// SHA-256 (hex) of the device's secret API key. The plain key is only
    /// ever shown once, to the installer that enrolled the device.
    /// Never serialised to API responses.
    /// </summary>
    [JsonIgnore]
    public string? ApiKeyHash { get; private set; }

    public DateTime? ApiKeyIssuedAtUtc { get; private set; }

    public bool HasApiKey => !string.IsNullOrEmpty(ApiKeyHash);

    private Device()
    {
    }

    public Device(
        Guid customerId,
        string name,
        string? hostname = null,
        string? operatingSystem = null)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException(
                "Customer ID is required.",
                nameof(customerId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Device name is required.",
                nameof(name));

        CustomerId = customerId;
        Name = name.Trim();
        Hostname = hostname?.Trim();
        OperatingSystem = operatingSystem?.Trim();
    }

    public void RecordHeartbeat(string? agentVersion = null)
    {
        LastSeenAtUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(agentVersion))
            AgentVersion = agentVersion.Trim();
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Device name is required.",
                nameof(name));

        Name = name.Trim();
    }

    public void AssignBackupPlan(Guid backupPlanId)
    {
        if (backupPlanId == Guid.Empty)
            throw new ArgumentException(
                "Backup plan ID is required.",
                nameof(backupPlanId));

        AssignedBackupPlanId = backupPlanId;
    }

    public void UnassignBackupPlan()
    {
        AssignedBackupPlanId = null;
    }

    public void SetApiKeyHash(string apiKeyHash)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash))
            throw new ArgumentException(
                "API key hash is required.",
                nameof(apiKeyHash));

        ApiKeyHash = apiKeyHash.Trim().ToLowerInvariant();
        ApiKeyIssuedAtUtc = DateTime.UtcNow;
    }

    public void RevokeApiKey()
    {
        ApiKeyHash = null;
        ApiKeyIssuedAtUtc = null;
    }

    public void UpdateDetails(
        string? hostname,
        string? operatingSystem)
    {
        if (!string.IsNullOrWhiteSpace(hostname))
            Hostname = hostname.Trim();

        if (!string.IsNullOrWhiteSpace(operatingSystem))
            OperatingSystem = operatingSystem.Trim();
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}
