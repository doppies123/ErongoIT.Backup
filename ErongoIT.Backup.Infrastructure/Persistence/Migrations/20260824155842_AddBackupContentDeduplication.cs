using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErongoIT.Backup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupContentDeduplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_backup_files_Sha256",
                table: "backup_files");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "backup_files");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "backup_files");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "backup_files");

            migrationBuilder.AddColumn<Guid>(
                name: "BackupContentId",
                table: "backup_files",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "backup_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_content", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_files_BackupContentId",
                table: "backup_files",
                column: "BackupContentId");

            migrationBuilder.CreateIndex(
                name: "IX_backup_content_Sha256",
                table: "backup_content",
                column: "Sha256",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_backup_files_backup_content_BackupContentId",
                table: "backup_files",
                column: "BackupContentId",
                principalTable: "backup_content",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_backup_files_backup_content_BackupContentId",
                table: "backup_files");

            migrationBuilder.DropTable(
                name: "backup_content");

            migrationBuilder.DropIndex(
                name: "IX_backup_files_BackupContentId",
                table: "backup_files");

            migrationBuilder.DropColumn(
                name: "BackupContentId",
                table: "backup_files");

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "backup_files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "backup_files",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "backup_files",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_backup_files_Sha256",
                table: "backup_files",
                column: "Sha256");
        }
    }
}
