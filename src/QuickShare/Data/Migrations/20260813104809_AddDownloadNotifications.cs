using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailOnDownload",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Notified",
                table: "download_tracking",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: downloads that happened before this feature existed must not
            // trigger a digest on first run.
            migrationBuilder.Sql("""UPDATE download_tracking SET "Notified" = TRUE;""");

            migrationBuilder.CreateIndex(
                name: "IX_download_tracking_Notified",
                table: "download_tracking",
                column: "Notified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_download_tracking_Notified",
                table: "download_tracking");

            migrationBuilder.DropColumn(
                name: "EmailOnDownload",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Notified",
                table: "download_tracking");
        }
    }
}
