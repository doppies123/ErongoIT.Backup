using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Customers;

public interface ICustomerService
{
    Task<IReadOnlyList<Customer>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<Customer?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Customer> CreateAsync(
        string name,
        string? contactEmail = null,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateContactEmailAsync(
        Guid id,
        string? contactEmail,
        CancellationToken cancellationToken = default);

    Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
