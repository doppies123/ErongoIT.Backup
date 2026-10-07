using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "backup_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backup_plans_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OperatingSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_devices_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "backup_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BytesSelected = table.Column<long>(type: "bigint", nullable: false),
                    BytesUploaded = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backup_jobs_backup_plans_BackupPlanId",
                        column: x => x.BackupPlanId,
                        principalTable: "backup_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_backup_jobs_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_backup_jobs_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "backup_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backup_files_backup_jobs_BackupJobId",
                        column: x => x.BackupJobId,
                        principalTable: "backup_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_files_BackupJobId",
                table: "backup_files",
                column: "BackupJobId");

            migrationBuilder.CreateIndex(
                name: "IX_backup_files_BackupJobId_RelativePath",
                table: "backup_files",
                columns: new[] { "BackupJobId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_backup_files_Sha256",
                table: "backup_files",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_backup_jobs_BackupPlanId_StartedAtUtc",
                table: "backup_jobs",
                columns: new[] { "BackupPlanId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_jobs_CustomerId_StartedAtUtc",
                table: "backup_jobs",
                columns: new[] { "CustomerId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_jobs_DeviceId_StartedAtUtc",
                table: "backup_jobs",
                columns: new[] { "DeviceId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_jobs_Status_StartedAtUtc",
                table: "backup_jobs",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_plans_CustomerId",
                table: "backup_plans",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_backup_plans_CustomerId_IsEnabled",
                table: "backup_plans",
                columns: new[] { "CustomerId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_plans_CustomerId_Name",
                table: "backup_plans",
                columns: new[] { "CustomerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_ContactEmail",
                table: "customers",
                column: "ContactEmail");

            migrationBuilder.CreateIndex(
                name: "IX_customers_Name",
                table: "customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_devices_CustomerId",
                table: "devices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_devices_CustomerId_Name",
                table: "devices",
                columns: new[] { "CustomerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_LastSeenAtUtc",
                table: "devices",
                column: "LastSeenAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_files");

            migrationBuilder.DropTable(
                name: "backup_jobs");

            migrationBuilder.DropTable(
                name: "backup_plans");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
