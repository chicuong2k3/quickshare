using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class CryptoPaymentRail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "payment_intents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderChargeId",
                table: "payment_intents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_ProviderChargeId",
                table: "payment_intents",
                column: "ProviderChargeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_intents_ProviderChargeId",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "ProviderChargeId",
                table: "payment_intents");
        }
    }
}
