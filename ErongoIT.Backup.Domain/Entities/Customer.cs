namespace ErongoIT.Backup.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; } = string.Empty;

    public string? ContactEmail { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private Customer()
    {
    }

    public Customer(string name, string? contactEmail = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required.", nameof(name));

        Name = name.Trim();
        ContactEmail = contactEmail?.Trim();
    }

    public void UpdateContactEmail(string? contactEmail)
    {
        ContactEmail = contactEmail?.Trim();
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}
