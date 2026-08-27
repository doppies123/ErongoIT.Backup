using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErongoIT.Backup.Infrastructure.Data.Configurations;

public sealed class BackupFileConfiguration
    : IEntityTypeConfiguration<BackupFile>
{
    public void Configure(EntityTypeBuilder<BackupFile> builder)
    {
        builder.ToTable("backup_files");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.BackupJobId)
            .IsRequired();

        builder.Property(x => x.BackupContentId)
            .IsRequired();

        builder.Property(x => x.RelativePath)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<BackupJob>()
            .WithMany()
            .HasForeignKey(x => x.BackupJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BackupContent>()
            .WithMany()
            .HasForeignKey(x => x.BackupContentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.BackupJobId);

        builder.HasIndex(x => x.BackupContentId);

        builder.HasIndex(x => new { x.BackupJobId, x.RelativePath })
            .IsUnique();
    }
}
