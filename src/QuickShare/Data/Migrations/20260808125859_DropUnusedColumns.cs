using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessHash",
                table: "files");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "files");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "files");

            migrationBuilder.DropColumn(
                name: "FileReference",
                table: "files");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "files");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "files");

            migrationBuilder.DropColumn(
                name: "EgressBytes",
                table: "deliveries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccessHash",
                table: "files",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ChannelId",
                table: "files",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "DocumentId",
                table: "files",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<byte[]>(
                name: "FileReference",
                table: "files",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "MessageId",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string[]>(
                name: "Tags",
                table: "files",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<long>(
                name: "EgressBytes",
                table: "deliveries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
