using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "R2UploadId",
                table: "upload_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "upload_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "deliveries",
                type: "text",
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "session_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<byte[]>(type: "bytea", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SchemeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_tickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_tickets_ExpiresAt",
                table: "session_tickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_session_tickets_Key",
                table: "session_tickets",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_tickets");

            migrationBuilder.DropColumn(
                name: "R2UploadId",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "deliveries");
        }
    }
}
