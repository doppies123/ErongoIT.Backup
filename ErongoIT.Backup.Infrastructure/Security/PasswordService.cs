using ErongoIT.Backup.Application.Security;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace ErongoIT.Backup.Infrastructure.Security;

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public string HashPassword(
        User user,
        string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException(
                "Password is required.",
                nameof(password));
        }

        return _passwordHasher.HashPassword(
            user,
            password);
    }

    public bool VerifyPassword(
        User user,
        string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password);

        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
