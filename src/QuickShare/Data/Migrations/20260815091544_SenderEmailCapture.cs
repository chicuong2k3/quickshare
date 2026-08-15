using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class SenderEmailCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SenderEmail",
                table: "upload_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderEmail",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SenderEmailConfirmed",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SenderEmailConsent",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SenderEmail",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "SenderEmail",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "SenderEmailConfirmed",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "SenderEmailConsent",
                table: "deliveries");
        }
    }
}
