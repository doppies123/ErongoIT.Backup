using ErongoIT.Backup.Application.Contracts;
using ErongoIT.Backup.Application.Maintenance;
using ErongoIT.Backup.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Maintenance;

public sealed class RetentionService : IRetentionService
{
    // Content created recently is never deleted, even if unreferenced,
    // so a backup that is registering files right now is never affected.
    private static readonly TimeSpan ContentGracePeriod = TimeSpan.FromDays(1);

    // Pending/Running jobs are only treated as abandoned after this long.
    private static readonly TimeSpan AbandonedJobAge = TimeSpan.FromDays(2);

    private const int BatchSize = 500;

    private readonly BackupDbContext _db;
    private readonly IBackupContentStorage _contentStorage;

    public RetentionService(
        BackupDbContext db,
        IBackupContentStorage contentStorage)
    {
        _db = db;
        _contentStorage = contentStorage;
    }

    public async Task<RetentionReport> RunAsync(
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var plans = await _db.BackupPlans
            .AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.RetentionDays })
            .ToListAsync(cancellationToken);

        var deleteIds = new List<Guid>();
        var summaries = new List<PlanRetentionSummary>();

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var retentionDays = Math.Max(plan.RetentionDays, 1);
            var cutoff = now.AddDays(-retentionDays);
            var abandonedCutoff = now - AbandonedJobAge;

            var jobs = await _db.BackupJobs
                .AsNoTracking()
                .Where(j => j.BackupPlanId == plan.Id)
                .Select(j => new
                {
                    j.Id,
                    j.DeviceId,
                    j.Status,
                    j.StartedAtUtc,
                    j.CompletedAtUtc
                })
                .ToListAsync(cancellationToken);

            // Never delete the newest completed backup of a device,
            // however old it is: it is the device's last restore point.
            var keepIds = jobs
                .Where(j => j.Status == "Completed")
                .GroupBy(j => j.DeviceId)
                .Select(g => g
                    .OrderByDescending(j => j.StartedAtUtc)
                    .First()
                    .Id)
                .ToHashSet();

            var expired = jobs
                .Where(j => !keepIds.Contains(j.Id))
                .Where(j =>
                {
                    var finishedAt = j.CompletedAtUtc ?? j.StartedAtUtc;

                    return j.Status switch
                    {
                        "Completed" or "Failed" or "Cancelled" =>
                            finishedAt < cutoff,

                        // Pending/Running that never finished: abandoned.
                        _ => j.StartedAtUtc < cutoff &&
                             j.StartedAtUtc < abandonedCutoff
                    };
                })
                .Select(j => j.Id)
                .ToList();

            deleteIds.AddRange(expired);

            summaries.Add(new PlanRetentionSummary(
                plan.Id,
                plan.Name,
                retentionDays,
                expired.Count,
                jobs.Count - expired.Count));
        }

        // Files that belong to the expired jobs.
        var filesDeleted = 0;

        foreach (var batch in deleteIds.Chunk(BatchSize))
        {
            filesDeleted += await _db.BackupFiles
                .CountAsync(
                    f => batch.Contains(f.BackupJobId),
                    cancellationToken);
        }

        // Content only a busy backup could be registering right now is
        // protected by the grace period; additionally skip content cleanup
        // entirely while a backup is actively running.
        var graceCutoff = now - ContentGracePeriod;

        var backupRunning = await _db.BackupJobs
            .AnyAsync(
                j => j.Status == "Running" &&
                     j.StartedAtUtc > now.AddHours(-24),
                cancellationToken);

        // Content that will be unreferenced once the expired jobs are gone.
        var candidates = await _db.BackupContents
            .AsNoTracking()
            .Where(c => c.CreatedAtUtc < graceCutoff)
            .Where(c => !_db.BackupFiles.Any(f =>
                f.BackupContentId == c.Id &&
                !deleteIds.Contains(f.BackupJobId)))
            .Select(c => new { c.Id, c.Sha256, c.SizeBytes })
            .ToListAsync(cancellationToken);

        if (dryRun)
        {
            return new RetentionReport(
                DryRun: true,
                RanAtUtc: now,
                JobsDeleted: deleteIds.Count,
                FilesDeleted: filesDeleted,
                ContentsDeleted: backupRunning ? 0 : candidates.Count,
                BytesFreed: backupRunning ? 0 : candidates.Sum(c => c.SizeBytes),
                ContentCleanupSkipped: backupRunning,
                Plans: summaries);
        }

        // 1. Delete expired jobs. backup_files rows go with them
        //    (ON DELETE CASCADE in the database).
        foreach (var batch in deleteIds.Chunk(BatchSize))
        {
            await _db.BackupJobs
                .Where(j => batch.Contains(j.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var contentsDeleted = 0;
        long bytesFreed = 0;

        if (!backupRunning)
        {
            // 2. Delete content that is now unreferenced. Re-check the
            //    reference at delete time so nothing in use is removed.
            foreach (var batch in candidates.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ids = batch.Select(c => c.Id).ToList();

                var stillUnreferenced = await _db.BackupContents
                    .Where(c => ids.Contains(c.Id))
                    .Where(c => !_db.BackupFiles.Any(f => f.BackupContentId == c.Id))
                    .Select(c => new { c.Id, c.Sha256, c.SizeBytes })
                    .ToListAsync(cancellationToken);

                if (stillUnreferenced.Count == 0)
                    continue;

                var deletableIds = stillUnreferenced
                    .Select(c => c.Id)
                    .ToList();

                await _db.BackupContents
                    .Where(c => deletableIds.Contains(c.Id))
                    .ExecuteDeleteAsync(cancellationToken);

                // Database row first, then the physical file: if deleting
                // the file fails, the leftover file is harmless.
                foreach (var content in stillUnreferenced)
                {
                    try
                    {
                        await _contentStorage.DeleteAsync(
                            content.Sha256,
                            cancellationToken);
                    }
                    catch (IOException)
                    {
                        // Leave it; it no longer affects backups or restores.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    contentsDeleted++;
                    bytesFreed += content.SizeBytes;
                }
            }
        }

        return new RetentionReport(
            DryRun: false,
            RanAtUtc: now,
            JobsDeleted: deleteIds.Count,
            FilesDeleted: filesDeleted,
            ContentsDeleted: contentsDeleted,
            BytesFreed: bytesFreed,
            ContentCleanupSkipped: backupRunning,
            Plans: summaries);
    }
}
