using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<User>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<User> CreateAsync(
        string username,
        string passwordHash,
        CancellationToken cancellationToken = default);

    Task<bool> ChangePasswordAsync(
        Guid id,
        string passwordHash,
        CancellationToken cancellationToken = default);

    Task<bool> RecordLoginAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
