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

            // Pre-existing rows all received the DEFAULT 0 — a unique index cannot
            // be created over duplicates. Backfill each such row with a unique code.
            // row_number() is safe here: real PayOS codes are Unix-epoch-based
            // (~10 digits), so 1..N never collides, and the upgrade flow re-rolls
            // on any collision anyway.
            migrationBuilder.Sql("""
                UPDATE payment_intents
                SET "PayOSOrderCode" = sub.new_code
                FROM (
                    SELECT "Id",
                           row_number() OVER (ORDER BY "CreatedAt", "Id") AS new_code
                    FROM payment_intents
                    WHERE "PayOSOrderCode" = 0
                ) sub
                WHERE payment_intents."Id" = sub."Id";
                """);

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
