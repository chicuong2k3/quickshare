using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTrial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "subscriptions");
        }
    }
}
