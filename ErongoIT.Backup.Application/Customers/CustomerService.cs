using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Customers;

public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repository;

    public CustomerService(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<Customer>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    public Task<Customer?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Customer> CreateAsync(
        string name,
        string? contactEmail = null,
        CancellationToken cancellationToken = default)
    {
        var customer = new Customer(name, contactEmail);

        await _repository.AddAsync(customer, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return customer;
    }

    public async Task<bool> UpdateContactEmailAsync(
        Guid id,
        string? contactEmail,
        CancellationToken cancellationToken = default)
    {
        var customer = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (customer is null)
            return false;

        customer.UpdateContactEmail(contactEmail);

        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var customer = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (customer is null)
            return false;

        customer.Activate();

        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var customer = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (customer is null)
            return false;

        customer.Deactivate();

        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }
}
