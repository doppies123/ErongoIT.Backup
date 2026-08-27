using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data.Repositories;

public sealed class BackupPlanRepository : IBackupPlanRepository
{
    private readonly BackupDbContext _db;

    public BackupPlanRepository(BackupDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BackupPlan>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.BackupPlans
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<BackupPlan?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.BackupPlans
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public Task<bool> ExistsByCustomerAndNameAsync(
        Guid customerId,
        string name,
        Guid? excludePlanId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.BackupPlans
            .Where(x =>
                x.CustomerId == customerId &&
                x.Name == name);

        if (excludePlanId.HasValue)
            query = query.Where(
                x => x.Id != excludePlanId.Value);

        return query.AnyAsync(cancellationToken);
    }

    public async Task AddAsync(
        BackupPlan backupPlan,
        CancellationToken cancellationToken = default)
    {
        await _db.BackupPlans.AddAsync(
            backupPlan,
            cancellationToken);
    }

    public void Remove(BackupPlan backupPlan)
    {
        _db.BackupPlans.Remove(backupPlan);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
