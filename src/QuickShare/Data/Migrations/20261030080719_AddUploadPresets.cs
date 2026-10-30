using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExpiryHours = table.Column<double>(type: "double precision", nullable: true),
                    Password = table.Column<string>(type: "text", nullable: true),
                    BurnAfterDownload = table.Column<bool>(type: "boolean", nullable: false),
                    PrivacyMode = table.Column<bool>(type: "boolean", nullable: false),
                    Recipients = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadPresets_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadPresets_OwnerId",
                table: "UploadPresets",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadPresets");
        }
    }
}
