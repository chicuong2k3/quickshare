using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyP2PUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_p2p_usage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UploadCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UnlockedUploads = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_p2p_usage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_daily_p2p_usage_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_p2p_usage_IpAddress_Date",
                table: "daily_p2p_usage",
                columns: new[] { "IpAddress", "Date" },
                unique: true,
                filter: "\"UserId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_daily_p2p_usage_UserId_Date",
                table: "daily_p2p_usage",
                columns: new[] { "UserId", "Date" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_p2p_usage");
        }
    }
}
