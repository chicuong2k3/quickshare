using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSessionTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SchemeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_tickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_tickets_ExpiresAt",
                table: "session_tickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_session_tickets_Key",
                table: "session_tickets",
                column: "Key",
                unique: true);
        }
    }
}
