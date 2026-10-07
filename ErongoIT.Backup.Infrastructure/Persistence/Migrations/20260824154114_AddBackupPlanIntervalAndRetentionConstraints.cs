using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupPlanIntervalAndRetentionConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_backup_plans_interval_minutes_min",
                table: "backup_plans",
                sql: "\"IntervalMinutes\" >= 15");

            migrationBuilder.AddCheckConstraint(
                name: "CK_backup_plans_retention_days_positive",
                table: "backup_plans",
                sql: "\"RetentionDays\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_backup_plans_interval_minutes_min",
                table: "backup_plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_backup_plans_retention_days_positive",
                table: "backup_plans");
        }
    }
}
