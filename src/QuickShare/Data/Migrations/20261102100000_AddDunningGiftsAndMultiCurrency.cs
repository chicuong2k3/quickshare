using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDunningGiftsAndMultiCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DunningAttemptCount",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDunningAttemptAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DunningGraceEndsAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateSnapshot",
                table: "payment_intents",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "subscription_gifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Billing = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_gifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscription_gifts_users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_gifts_PaymentIntentId",
                table: "subscription_gifts",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_gifts_SenderUserId",
                table: "subscription_gifts",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_gifts_Token",
                table: "subscription_gifts",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_gifts");

            migrationBuilder.DropColumn(
                name: "DunningAttemptCount",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "LastDunningAttemptAt",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "DunningGraceEndsAt",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSnapshot",
                table: "payment_intents");
        }
    }
}
