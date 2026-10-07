using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupPlanScheduleDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScheduleDayOfWeek",
                table: "backup_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScheduleTimeMinutes",
                table: "backup_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_backup_plans_schedule_day_valid",
                table: "backup_plans",
                sql: "\"ScheduleDayOfWeek\" >= 0 AND \"ScheduleDayOfWeek\" <= 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_backup_plans_schedule_time_valid",
                table: "backup_plans",
                sql: "\"ScheduleTimeMinutes\" >= 0 AND \"ScheduleTimeMinutes\" <= 1439");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_backup_plans_schedule_day_valid",
                table: "backup_plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_backup_plans_schedule_time_valid",
                table: "backup_plans");

            migrationBuilder.DropColumn(
                name: "ScheduleDayOfWeek",
                table: "backup_plans");

            migrationBuilder.DropColumn(
                name: "ScheduleTimeMinutes",
                table: "backup_plans");
        }
    }
}
