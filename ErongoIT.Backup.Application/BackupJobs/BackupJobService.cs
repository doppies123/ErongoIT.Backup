using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Domain.Enums;

namespace ErongoIT.Backup.Application.BackupJobs;

public sealed class BackupJobService : IBackupJobService
{
    private readonly IBackupJobRepository _jobs;
    private readonly ICustomerRepository _customers;
    private readonly IDeviceRepository _devices;
    private readonly IBackupPlanRepository _plans;

    public BackupJobService(
        IBackupJobRepository jobs,
        ICustomerRepository customers,
        IDeviceRepository devices,
        IBackupPlanRepository plans)
    {
        _jobs = jobs;
        _customers = customers;
        _devices = devices;
        _plans = plans;
    }

    public Task<IReadOnlyList<BackupJob>> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return _jobs.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BackupJob>> GetRestorePointsByDeviceIdAsync(
        Guid deviceId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException(
                "Device ID is required.",
                nameof(deviceId));

        if (fromUtc.HasValue &&
            toUtc.HasValue &&
            fromUtc.Value > toUtc.Value)
        {
            throw new ArgumentException(
                "The start date must be earlier than or equal to the end date.");
        }

        var jobs = await _jobs.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);

        var restorePoints = jobs
            .Where(x => string.Equals(
                x.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase));

        if (fromUtc.HasValue)
        {
            restorePoints = restorePoints.Where(
                x => x.CompletedAtUtc.HasValue &&
                     x.CompletedAtUtc.Value >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            restorePoints = restorePoints.Where(
                x => x.CompletedAtUtc.HasValue &&
                     x.CompletedAtUtc.Value <= toUtc.Value);
        }

        return restorePoints
            .OrderByDescending(x => x.CompletedAtUtc)
            .ToList();
    }

    public Task<BackupJob?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _jobs.GetByIdAsync(
            id,
            cancellationToken);
    }

    public async Task<BackupJob> CreateAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        BackupType type,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(
            customerId,
            cancellationToken);

        if (customer is null)
            throw new InvalidOperationException(
                "Customer was not found.");

        var device = await _devices.GetByIdAsync(
            deviceId,
            cancellationToken);

        if (device is null)
            throw new InvalidOperationException(
                "Device was not found.");

        if (device.CustomerId != customerId)
            throw new InvalidOperationException(
                "Device does not belong to the specified customer.");

        var plan = await _plans.GetByIdAsync(
            backupPlanId,
            cancellationToken);

        if (plan is null)
            throw new InvalidOperationException(
                "Backup plan was not found.");

        if (plan.CustomerId != customerId)
            throw new InvalidOperationException(
                "Backup plan does not belong to the specified customer.");

        if (!plan.IsEnabled)
            throw new InvalidOperationException(
                "Backup plan is disabled.");

        var job = new BackupJob(
            customerId,
            deviceId,
            backupPlanId,
            type);

        await _jobs.AddAsync(
            job,
            cancellationToken);

        await _jobs.SaveChangesAsync(
            cancellationToken);

        return job;
    }

    public async Task<bool> StartAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(
            id,
            cancellationToken);

        if (job is null)
            return false;

        job.Start();

        await _jobs.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> CompleteAsync(
        Guid id,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(
            id,
            cancellationToken);

        if (job is null)
            return false;

        job.Complete(
            bytesSelected,
            bytesUploaded);

        await _jobs.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> FailAsync(
        Guid id,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(
            id,
            cancellationToken);

        if (job is null)
            return false;

        job.Fail(errorMessage);

        await _jobs.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(
            id,
            cancellationToken);

        if (job is null)
            return false;

        job.Cancel();

        await _jobs.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<int> RecoverStaleJobsAsync(
        Guid deviceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException(
                "Device ID is required.",
                nameof(deviceId));

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The stale-job timeout must be greater than zero.");

        var jobs = await _jobs.GetActiveJobsAsync(
            deviceId,
            cancellationToken);

        var cutoffUtc = DateTime.UtcNow - timeout;
        var recovered = 0;

        foreach (var job in jobs)
        {
            if (job.StartedAtUtc > cutoffUtc)
                continue;

            job.Abandon(
                $"Backup job was abandoned because it remained {job.Status.ToLowerInvariant()} beyond the allowed timeout of {timeout.TotalMinutes:0} minutes.");

            recovered++;
        }

        if (recovered > 0)
        {
            await _jobs.SaveChangesAsync(
                cancellationToken);
        }

        return recovered;
    }

    public async Task<int> ClearHistoryAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException(
                "Device ID is required.",
                nameof(deviceId));

        return await _jobs.DeleteHistoryByDeviceIdAsync(
            deviceId,
            cancellationToken);
    }
}
