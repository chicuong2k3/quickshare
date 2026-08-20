using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalHoldAndScopeRulesAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetentionDays",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RetentionNoExtend",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AllowedCountries",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedIpRanges",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedCountries",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalHoldAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalHoldById",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "deliveries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_LegalHoldAt",
                table: "deliveries",
                column: "LegalHoldAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_deliveries_LegalHoldAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "RetentionDays",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RetentionNoExtend",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AllowedCountries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "AllowedIpRanges",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "BlockedCountries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "LegalHoldAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "LegalHoldById",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "LegalHoldReason",
                table: "deliveries");
        }
    }
}
