using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryEmailOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailOtpEmail",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailOtpEnabled",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailOtpExpiresAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailOtpHash",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailOtpLastSentAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailOtpUsedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailOtpEmail",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EmailOtpEnabled",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EmailOtpExpiresAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EmailOtpHash",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EmailOtpLastSentAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EmailOtpUsedAt",
                table: "deliveries");
        }
    }
}
