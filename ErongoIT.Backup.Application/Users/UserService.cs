using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _repository;

    public UserService(IUserRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<User>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    public Task<User?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(
            id,
            cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByUsernameAsync(
            username,
            cancellationToken);
    }

    public async Task<User> CreateAsync(
        string username,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var existingUser =
            await _repository.GetByUsernameAsync(
                username,
                cancellationToken);

        if (existingUser is not null)
        {
            throw new InvalidOperationException(
                "A user with this username already exists.");
        }

        var user = new User(
            username,
            passwordHash);

        await _repository.AddAsync(
            user,
            cancellationToken);

        await _repository.SaveChangesAsync(
            cancellationToken);

        return user;
    }

    public async Task<bool> ChangePasswordAsync(
        Guid id,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (user is null)
            return false;

        user.ChangePassword(passwordHash);

        await _repository.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> RecordLoginAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (user is null)
            return false;

        user.RecordLogin();

        await _repository.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (user is null)
            return false;

        user.Activate();

        await _repository.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (user is null)
            return false;

        user.Deactivate();

        await _repository.SaveChangesAsync(
            cancellationToken);

        return true;
    }
}
