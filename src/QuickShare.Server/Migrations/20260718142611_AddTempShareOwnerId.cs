using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTempShareOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "temp_shares",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_temp_shares_OwnerId",
                table: "temp_shares",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_temp_shares_users_OwnerId",
                table: "temp_shares",
                column: "OwnerId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_temp_shares_users_OwnerId",
                table: "temp_shares");

            migrationBuilder.DropIndex(
                name: "IX_temp_shares_OwnerId",
                table: "temp_shares");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "temp_shares");
        }
    }
}
