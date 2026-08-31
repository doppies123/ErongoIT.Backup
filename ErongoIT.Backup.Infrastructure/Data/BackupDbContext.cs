using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data;

public sealed class BackupDbContext : DbContext
{
    public BackupDbContext(
        DbContextOptions<BackupDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<BackupPlan> BackupPlans => Set<BackupPlan>();

    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();

    public DbSet<BackupFile> BackupFiles => Set<BackupFile>();

    public DbSet<BackupContent> BackupContents => Set<BackupContent>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(BackupDbContext).Assembly);
    }
}
