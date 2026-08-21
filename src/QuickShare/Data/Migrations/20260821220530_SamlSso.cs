using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class SamlSso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SamlCertificate",
                table: "WorkspaceSsoConfigs",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SamlSsoUrl",
                table: "WorkspaceSsoConfigs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SamlAssertionsSeen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssertionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SamlAssertionsSeen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SamlAssertionsSeen_AssertionId",
                table: "SamlAssertionsSeen",
                column: "AssertionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SamlAssertionsSeen_ExpiresAt",
                table: "SamlAssertionsSeen",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SamlAssertionsSeen");

            migrationBuilder.DropColumn(
                name: "SamlCertificate",
                table: "WorkspaceSsoConfigs");

            migrationBuilder.DropColumn(
                name: "SamlSsoUrl",
                table: "WorkspaceSsoConfigs");
        }
    }
}
