using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Persistence;

public interface IDeviceRepository
{
    Task<IReadOnlyList<Device>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<Device?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCustomerAndNameAsync(
        Guid customerId,
        string name,
        Guid? excludeDeviceId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Device device,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
