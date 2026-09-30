using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Devices;

public interface IDeviceService
{
    Task<IReadOnlyList<Device>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<Device?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Device> CreateAsync(
        Guid customerId,
        string name,
        string? hostname = null,
        string? operatingSystem = null,
        CancellationToken cancellationToken = default);

    Task<bool> RenameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken = default);

    Task<bool> RecordHeartbeatAsync(
        Guid id,
        string? agentVersion = null,
        CancellationToken cancellationToken = default);

    Task<bool> AssignBackupPlanAsync(
        Guid deviceId,
        Guid backupPlanId,
        CancellationToken cancellationToken = default);

    Task<bool> UnassignBackupPlanAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a PC for a customer (or re-enrols an existing device with
    /// the same name) and issues a new secret API key. The plain key is
    /// returned once and never stored.
    /// </summary>
    Task<DeviceEnrollmentResult> EnrollAsync(
        Guid customerId,
        string name,
        string? hostname,
        string? operatingSystem,
        Guid? backupPlanId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the device if the key is valid and the device is active.</summary>
    Task<Device?> ValidateApiKeyAsync(
        Guid deviceId,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeApiKeyAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceEnrollmentResult(
    Device Device,
    string ApiKey,
    bool IsNewDevice);
