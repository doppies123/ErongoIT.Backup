using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly BackupDbContext _db;

    public CustomerRepository(BackupDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Customers
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Customer?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.Customers
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        Customer customer,
        CancellationToken cancellationToken = default)
    {
        await _db.Customers.AddAsync(customer, cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
