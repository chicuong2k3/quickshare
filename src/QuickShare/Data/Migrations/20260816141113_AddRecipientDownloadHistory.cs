using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientDownloadHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrackMyDownloads",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "download_tracking",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_tracking_UserId",
                table: "download_tracking",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_download_tracking_UserId",
                table: "download_tracking");

            migrationBuilder.DropColumn(
                name: "TrackMyDownloads",
                table: "users");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "download_tracking");
        }
    }
}
