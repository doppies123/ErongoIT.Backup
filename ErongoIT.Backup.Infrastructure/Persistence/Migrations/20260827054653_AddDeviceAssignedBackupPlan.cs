using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceAssignedBackupPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedBackupPlanId",
                table: "devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_AssignedBackupPlanId",
                table: "devices",
                column: "AssignedBackupPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_devices_backup_plans_AssignedBackupPlanId",
                table: "devices",
                column: "AssignedBackupPlanId",
                principalTable: "backup_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_devices_backup_plans_AssignedBackupPlanId",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_devices_AssignedBackupPlanId",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "AssignedBackupPlanId",
                table: "devices");
        }
    }
}
