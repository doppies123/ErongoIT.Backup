using ErongoIT.Backup.Application.Contracts;
using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Application.Security;
using ErongoIT.Backup.Infrastructure.Data.Repositories;
using ErongoIT.Backup.Infrastructure.Security;
using ErongoIT.Backup.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErongoIT.Backup.Infrastructure.Data;

public static class BackupDatabaseExtensions
{
    public static IServiceCollection AddBackupDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "PostgreSQL connection string is required.",
                nameof(connectionString));

        services.AddDbContext<BackupDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IBackupPlanRepository, BackupPlanRepository>();
        services.AddScoped<IBackupJobRepository, BackupJobRepository>();
        services.AddScoped<IBackupFileRepository, BackupFileRepository>();
        services.AddScoped<IBackupContentRepository, BackupContentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddSingleton<IPasswordService, PasswordService>();

        return services;
    }

    public static IServiceCollection AddBackupStorage(
        this IServiceCollection services,
        string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "Backup storage root path is required.",
                nameof(rootPath));

        services.AddSingleton<IBackupStorage>(
            new FileSystemBackupStorage(rootPath));

        services.AddSingleton<IBackupContentStorage>(
            new FileSystemBackupContentStorage(rootPath));

        return services;
    }
}
