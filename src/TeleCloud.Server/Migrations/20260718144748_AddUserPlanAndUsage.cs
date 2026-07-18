using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeleCloud.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPlanAndUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "free");

            migrationBuilder.CreateTable(
                name: "user_usage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalBytesUploaded = table.Column<long>(type: "bigint", nullable: false),
                    TransferCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_usage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_usage_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_usage_UserId_PeriodStart",
                table: "user_usage",
                columns: new[] { "UserId", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_usage");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "users");
        }
    }
}
