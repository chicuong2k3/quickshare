using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recipient_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UseCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipient_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recipient_contacts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recipient_contacts_UserId_Email",
                table: "recipient_contacts",
                columns: new[] { "UserId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipient_contacts_UserId_LastUsedAt",
                table: "recipient_contacts",
                columns: new[] { "UserId", "LastUsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recipient_contacts");
        }
    }
}
