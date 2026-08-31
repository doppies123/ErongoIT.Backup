using ErongoIT.Backup.Domain.Entities;

namespace ErongoIT.Backup.Application.Security;

public interface IPasswordService
{
    string HashPassword(
        User user,
        string password);

    bool VerifyPassword(
        User user,
        string password);
}
