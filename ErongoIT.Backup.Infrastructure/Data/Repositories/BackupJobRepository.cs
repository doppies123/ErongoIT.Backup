using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data.Repositories;

public sealed class BackupJobRepository : IBackupJobRepository
{
    private readonly BackupDbContext _db;

    public BackupJobRepository(BackupDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BackupJob>> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return await _db.BackupJobs
            .AsNoTracking()
            .Where(x => x.DeviceId == deviceId)
            .OrderByDescending(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<BackupJob?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.BackupJobs
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task<IReadOnlyList<BackupJob>> GetActiveJobsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return await _db.BackupJobs
            .Where(x =>
                x.DeviceId == deviceId &&
                (x.Status == "Pending" ||
                 x.Status == "Running"))
            .OrderBy(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> DeleteHistoryByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var jobs = await _db.BackupJobs
            .Where(x =>
                x.DeviceId == deviceId &&
                x.Status != "Pending" &&
                x.Status != "Running")
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
            return 0;

        _db.BackupJobs.RemoveRange(jobs);

        await _db.SaveChangesAsync(cancellationToken);

        return jobs.Count;
    }

    public async Task AddAsync(
        BackupJob backupJob,
        CancellationToken cancellationToken = default)
    {
        await _db.BackupJobs.AddAsync(
            backupJob,
            cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
