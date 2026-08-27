using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErongoIT.Backup.Infrastructure.Data.Configurations;

public sealed class BackupJobConfiguration : IEntityTypeConfiguration<BackupJob>
{
    public void Configure(EntityTypeBuilder<BackupJob> builder)
    {
        builder.ToTable("backup_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.DeviceId)
            .IsRequired();

        builder.Property(x => x.BackupPlanId)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.StartedAtUtc)
            .IsRequired();

        builder.Property(x => x.CompletedAtUtc);

        builder.Property(x => x.BytesSelected)
            .IsRequired();

        builder.Property(x => x.BytesUploaded)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(4000);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BackupPlan>()
            .WithMany()
            .HasForeignKey(x => x.BackupPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CustomerId, x.StartedAtUtc });

        builder.HasIndex(x => new { x.DeviceId, x.StartedAtUtc });

        builder.HasIndex(x => new { x.BackupPlanId, x.StartedAtUtc });

        builder.HasIndex(x => new { x.Status, x.StartedAtUtc });
    }
}
