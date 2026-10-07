using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupPlanScheduleType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScheduleType",
                table: "backup_plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_backup_plans_schedule_type_valid",
                table: "backup_plans",
                sql: "\"ScheduleType\" IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_backup_plans_schedule_type_valid",
                table: "backup_plans");

            migrationBuilder.DropColumn(
                name: "ScheduleType",
                table: "backup_plans");
        }
    }
}
