using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErongoIT.Backup.Infrastructure.Data.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Hostname)
            .HasMaxLength(255);

        builder.Property(x => x.OperatingSystem)
            .HasMaxLength(100);

        builder.Property(x => x.AgentVersion)
            .HasMaxLength(50);

        builder.Property(x => x.LastSeenAtUtc);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.AssignedBackupPlanId);

        builder.Property(x => x.ApiKeyHash)
            .HasMaxLength(64);

        builder.Property(x => x.ApiKeyIssuedAtUtc);

        builder.Ignore(x => x.HasApiKey);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BackupPlan>()
            .WithMany()
            .HasForeignKey(x => x.AssignedBackupPlanId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.CustomerId, x.Name })
            .IsUnique();

        builder.HasIndex(x => x.CustomerId);

        builder.HasIndex(x => x.LastSeenAtUtc);

        builder.HasIndex(x => x.AssignedBackupPlanId);
    }
}
