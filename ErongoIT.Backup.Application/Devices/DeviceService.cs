using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Devices;

public sealed class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _devices;
    private readonly ICustomerRepository _customers;
    private readonly IBackupPlanRepository _backupPlans;

    public DeviceService(
        IDeviceRepository devices,
        ICustomerRepository customers,
        IBackupPlanRepository backupPlans)
    {
        _devices = devices;
        _customers = customers;
        _backupPlans = backupPlans;
    }

    public Task<IReadOnlyList<Device>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _devices.GetByCustomerIdAsync(
            customerId,
            cancellationToken);
    }

    public Task<Device?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _devices.GetByIdAsync(
            id,
            cancellationToken);
    }

    public async Task<Device> CreateAsync(
        Guid customerId,
        string name,
        string? hostname = null,
        string? operatingSystem = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(
            customerId,
            cancellationToken);

        if (customer is null)
            throw new InvalidOperationException(
                "Customer was not found.");

        if (await _devices.ExistsByCustomerAndNameAsync(
                customerId,
                name,
                cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                "A device with this name already exists for the customer.");
        }

        var device = new Device(
            customerId,
            name,
            hostname,
            operatingSystem);

        await _devices.AddAsync(device, cancellationToken);
        await _devices.SaveChangesAsync(cancellationToken);

        return device;
    }

    public async Task<bool> RenameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(
            id,
            cancellationToken);

        if (device is null)
            return false;

        if (await _devices.ExistsByCustomerAndNameAsync(
                device.CustomerId,
                name,
                id,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "A device with this name already exists for the customer.");
        }

        device.Rename(name);

        await _devices.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> RecordHeartbeatAsync(
        Guid id,
        string? agentVersion = null,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(
            id,
            cancellationToken);

        if (device is null)
            return false;

        device.RecordHeartbeat(agentVersion);

        await _devices.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> AssignBackupPlanAsync(
        Guid deviceId,
        Guid backupPlanId,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(
            deviceId,
            cancellationToken);

        if (device is null)
            return false;

        var plan = await _backupPlans.GetByIdAsync(
            backupPlanId,
            cancellationToken);

        if (plan is null)
            throw new InvalidOperationException(
                "Backup plan was not found.");

        if (plan.CustomerId != device.CustomerId)
            throw new InvalidOperationException(
                "The backup plan does not belong to the device's customer.");

        device.AssignBackupPlan(backupPlanId);

        await _devices.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> UnassignBackupPlanAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(
            deviceId,
            cancellationToken);

        if (device is null)
            return false;

        device.UnassignBackupPlan();

        await _devices.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(
            id,
            cancellationToken);

        if (device is null)
            return false;

        device.Activate();

        await _devices.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(
            id,
            cancellationToken);

        if (device is null)
            return false;

        device.Deactivate();

        await _devices.SaveChangesAsync(cancellationToken);

        return true;
    }
}
