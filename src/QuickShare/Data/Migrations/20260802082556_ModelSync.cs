using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DailyBandwidthBonus",
                table: "user_usage",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "daily_ad_tracking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AdCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TokenHashes = table.Column<string[]>(type: "text[]", nullable: false, defaultValue: new string[0])
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_ad_tracking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_daily_ad_tracking_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_ad_tracking_UserId_Date",
                table: "daily_ad_tracking",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_ad_tracking");

            migrationBuilder.DropColumn(
                name: "DailyBandwidthBonus",
                table: "user_usage");
        }
    }
}
