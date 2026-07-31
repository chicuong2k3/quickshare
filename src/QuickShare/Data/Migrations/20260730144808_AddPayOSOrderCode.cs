using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class AddPayOSOrderCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PayOSOrderCode",
                table: "payment_intents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_PayOSOrderCode",
                table: "payment_intents",
                column: "PayOSOrderCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_intents_PayOSOrderCode",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "PayOSOrderCode",
                table: "payment_intents");
        }
    }
}
