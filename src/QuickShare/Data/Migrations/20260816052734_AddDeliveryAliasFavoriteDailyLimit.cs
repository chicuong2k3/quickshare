using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryAliasFavoriteDailyLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "deliveries",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyDownloadLimit",
                table: "deliveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_Alias",
                table: "deliveries",
                column: "Alias",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_deliveries_Alias",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "DailyDownloadLimit",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "deliveries");
        }
    }
}
