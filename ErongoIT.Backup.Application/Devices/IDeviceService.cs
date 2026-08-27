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

    Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
