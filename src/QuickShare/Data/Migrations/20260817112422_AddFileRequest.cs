using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileRequestId",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "file_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxTotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxFiles = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_requests_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_FileRequestId",
                table: "deliveries",
                column: "FileRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_file_requests_Code",
                table: "file_requests",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_requests_ExpiresAt",
                table: "file_requests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_requests_OwnerId",
                table: "file_requests",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_deliveries_file_requests_FileRequestId",
                table: "deliveries",
                column: "FileRequestId",
                principalTable: "file_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_deliveries_file_requests_FileRequestId",
                table: "deliveries");

            migrationBuilder.DropTable(
                name: "file_requests");

            migrationBuilder.DropIndex(
                name: "IX_deliveries_FileRequestId",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "FileRequestId",
                table: "deliveries");
        }
    }
}
