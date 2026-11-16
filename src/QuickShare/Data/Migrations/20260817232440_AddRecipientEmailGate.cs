using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientEmailGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MarketingConsent",
                table: "download_tracking",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientEmail",
                table: "download_tracking",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireRecipientEmail",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarketingConsent",
                table: "download_tracking");

            migrationBuilder.DropColumn(
                name: "RecipientEmail",
                table: "download_tracking");

            migrationBuilder.DropColumn(
                name: "RequireRecipientEmail",
                table: "deliveries");
        }
    }
}
