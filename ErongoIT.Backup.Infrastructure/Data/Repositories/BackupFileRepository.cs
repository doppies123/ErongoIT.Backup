using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data.Repositories;

public sealed class BackupFileRepository : IBackupFileRepository
{
    private readonly BackupDbContext _db;

    public BackupFileRepository(BackupDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BackupFile>> GetByBackupJobIdAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        return await _db.BackupFiles
            .AsNoTracking()
            .Where(x => x.BackupJobId == backupJobId)
            .OrderBy(x => x.RelativePath)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BackupFile>> GetByIdsAsync(
        Guid backupJobId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return Array.Empty<BackupFile>();

        var distinctIds = ids
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctIds.Count == 0)
            return Array.Empty<BackupFile>();

        return await _db.BackupFiles
            .AsNoTracking()
            .Where(x =>
                x.BackupJobId == backupJobId &&
                distinctIds.Contains(x.Id))
            .OrderBy(x => x.RelativePath)
            .ToListAsync(cancellationToken);
    }

    public Task<BackupFile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.BackupFiles
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>>
        GetFileCountsByBackupJobIdsAsync(
            IReadOnlyCollection<Guid> backupJobIds,
            CancellationToken cancellationToken = default)
    {
        if (backupJobIds.Count == 0)
            return new Dictionary<Guid, int>();

        var ids = backupJobIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, int>();

        var counts = await _db.BackupFiles
            .AsNoTracking()
            .Where(x => ids.Contains(x.BackupJobId))
            .GroupBy(x => x.BackupJobId)
            .Select(x => new
            {
                BackupJobId = x.Key,
                Count = x.Count()
            })
            .ToListAsync(cancellationToken);

        var result = counts.ToDictionary(
            x => x.BackupJobId,
            x => x.Count);

        // Newer agents record file versions instead: count the files each
        // backup added or changed.
        var versionCounts = await _db.FileVersions
            .AsNoTracking()
            .Where(x => x.BackupJobId != null && ids.Contains(x.BackupJobId.Value))
            .GroupBy(x => x.BackupJobId!.Value)
            .Select(x => new
            {
                BackupJobId = x.Key,
                Count = x.Count()
            })
            .ToListAsync(cancellationToken);

        foreach (var item in versionCounts)
        {
            result[item.BackupJobId] =
                result.GetValueOrDefault(item.BackupJobId) + item.Count;
        }

        return result;
    }

    public async Task AddAsync(
        BackupFile backupFile,
        CancellationToken cancellationToken = default)
    {
        await _db.BackupFiles.AddAsync(
            backupFile,
            cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
