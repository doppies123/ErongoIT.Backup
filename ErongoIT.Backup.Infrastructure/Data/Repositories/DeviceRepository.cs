using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data.Repositories;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly BackupDbContext _db;

    public DeviceRepository(BackupDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Device>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Devices
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Device?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.Devices
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<bool> ExistsByCustomerAndNameAsync(
        Guid customerId,
        string name,
        Guid? excludeDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Devices
            .Where(x =>
                x.CustomerId == customerId &&
                x.Name == name);

        if (excludeDeviceId.HasValue)
            query = query.Where(x => x.Id != excludeDeviceId.Value);

        return query.AnyAsync(cancellationToken);
    }

    public async Task AddAsync(
        Device device,
        CancellationToken cancellationToken = default)
    {
        await _db.Devices.AddAsync(device, cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
