using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErongoIT.Backup.Infrastructure.Data.Configurations;

public sealed class BackupContentConfiguration
    : IEntityTypeConfiguration<BackupContent>
{
    public void Configure(EntityTypeBuilder<BackupContent> builder)
    {
        builder.ToTable("backup_content");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Sha256)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.SizeBytes)
            .IsRequired();

        builder.Property(x => x.StoragePath)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.Sha256)
            .IsUnique();
    }
}
