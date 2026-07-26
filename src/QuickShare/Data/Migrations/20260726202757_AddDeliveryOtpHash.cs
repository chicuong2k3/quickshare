using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOtpHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Price",
                table: "deliveries");

            migrationBuilder.RenameColumn(
                name: "PaymentStatus",
                table: "deliveries",
                newName: "OtpHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OtpHash",
                table: "deliveries",
                newName: "PaymentStatus");

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "deliveries",
                type: "numeric",
                nullable: true);
        }
    }
}
