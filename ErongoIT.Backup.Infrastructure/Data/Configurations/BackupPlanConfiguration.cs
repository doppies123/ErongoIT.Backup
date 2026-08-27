using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErongoIT.Backup.Infrastructure.Data.Configurations;

public sealed class BackupPlanConfiguration : IEntityTypeConfiguration<BackupPlan>
{
    public void Configure(EntityTypeBuilder<BackupPlan> builder)
    {
        builder.ToTable("backup_plans", table =>
        {
            table.HasCheckConstraint(
                "CK_backup_plans_interval_minutes_min",
                "\"IntervalMinutes\" >= 15");

            table.HasCheckConstraint(
                "CK_backup_plans_retention_days_positive",
                "\"RetentionDays\" > 0");

            table.HasCheckConstraint(
                "CK_backup_plans_schedule_type_valid",
                "\"ScheduleType\" IN (1, 2, 3)");

            table.HasCheckConstraint(
                "CK_backup_plans_schedule_time_valid",
                "\"ScheduleTimeMinutes\" >= 0 AND \"ScheduleTimeMinutes\" <= 1439");

            table.HasCheckConstraint(
                "CK_backup_plans_schedule_day_valid",
                "\"ScheduleDayOfWeek\" >= 0 AND \"ScheduleDayOfWeek\" <= 6");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ScheduleType)
            .IsRequired();

        builder.Property(x => x.IntervalMinutes)
            .IsRequired();

        builder.Property(x => x.ScheduleTimeMinutes)
            .IsRequired();

        builder.Property(x => x.ScheduleDayOfWeek)
            .IsRequired();

        builder.Property(x => x.RetentionDays)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .IsRequired();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.CustomerId, x.Name })
            .IsUnique();

        builder.HasIndex(x => x.CustomerId);

        builder.HasIndex(x => new { x.CustomerId, x.IsEnabled });
    }
}
