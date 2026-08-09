using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandAccent",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "BrandLogo",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "BrandMessage",
                table: "deliveries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandAccent",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogo",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandMessage",
                table: "deliveries",
                type: "text",
                nullable: true);
        }
    }
}
