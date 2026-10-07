using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErongoIT.Backup.Infrastructure.Data.Configurations;

public sealed class FileVersionConfiguration
    : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("backup_file_versions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.DeviceId)
            .IsRequired();

        builder.Property(x => x.Path)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.Folder)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.BackupContentId)
            .IsRequired();

        builder.Property(x => x.SizeBytes)
            .IsRequired();

        builder.Property(x => x.LastWriteUtc);

        builder.Property(x => x.BackupJobId);

        builder.Property(x => x.ValidFromUtc)
            .IsRequired();

        builder.Property(x => x.ValidToUtc);

        builder.Ignore(x => x.IsCurrent);

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BackupContent>()
            .WithMany()
            .HasForeignKey(x => x.BackupContentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The backup job is only informative; versions outlive old jobs.
        builder.HasOne<BackupJob>()
            .WithMany()
            .HasForeignKey(x => x.BackupJobId)
            .OnDelete(DeleteBehavior.SetNull);

        // At most one current version per file.
        builder.HasIndex(x => new { x.DeviceId, x.Path })
            .HasFilter("\"ValidToUtc\" IS NULL")
            .IsUnique();

        // Point-in-time lookups and folder listings.
        builder.HasIndex(x => new { x.DeviceId, x.Path, x.ValidFromUtc });

        builder.HasIndex(x => new { x.DeviceId, x.Folder });

        builder.HasIndex(x => x.BackupContentId);

        builder.HasIndex(x => x.BackupJobId);

        builder.HasIndex(x => x.ValidToUtc);
    }
}
