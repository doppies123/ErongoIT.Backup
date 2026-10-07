using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BackupRequestedAtUtc",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "backup_file_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Folder = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BackupContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastWriteUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BackupJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_file_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backup_file_versions_backup_content_BackupContentId",
                        column: x => x.BackupContentId,
                        principalTable: "backup_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_backup_file_versions_backup_jobs_BackupJobId",
                        column: x => x.BackupJobId,
                        principalTable: "backup_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_backup_file_versions_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_file_versions_BackupContentId",
                table: "backup_file_versions",
                column: "BackupContentId");

            migrationBuilder.CreateIndex(
                name: "IX_backup_file_versions_BackupJobId",
                table: "backup_file_versions",
                column: "BackupJobId");

            migrationBuilder.CreateIndex(
                name: "IX_backup_file_versions_DeviceId_Folder",
                table: "backup_file_versions",
                columns: new[] { "DeviceId", "Folder" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_file_versions_DeviceId_Path",
                table: "backup_file_versions",
                columns: new[] { "DeviceId", "Path" },
                unique: true,
                filter: "\"ValidToUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_backup_file_versions_DeviceId_Path_ValidFromUtc",
                table: "backup_file_versions",
                columns: new[] { "DeviceId", "Path", "ValidFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_file_versions_ValidToUtc",
                table: "backup_file_versions",
                column: "ValidToUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_file_versions");

            migrationBuilder.DropColumn(
                name: "BackupRequestedAtUtc",
                table: "devices");
        }
    }
}
