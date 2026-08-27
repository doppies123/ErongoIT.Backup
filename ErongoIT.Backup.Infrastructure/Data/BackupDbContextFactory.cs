using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ErongoIT.Backup.Infrastructure.Data;

public sealed class BackupDbContextFactory
    : IDesignTimeDbContextFactory<BackupDbContext>
{
    public BackupDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ERONGOIT_BACKUP_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Environment variable 'ERONGOIT_BACKUP_CONNECTION_STRING' " +
                "must be configured for Entity Framework Core design-time operations.");
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<BackupDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new BackupDbContext(
            optionsBuilder.Options);
    }
}
