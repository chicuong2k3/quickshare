using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class AddFoldersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_folders_Path",
                table: "folders",
                column: "Path",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                INSERT INTO folders (""Id"", ""Path"", ""CreatedAt"")
                SELECT gen_random_uuid(), f.""Folder"", NOW()
                FROM (SELECT DISTINCT ""Folder"" FROM files) f
                ON CONFLICT (""Path"") DO NOTHING;

                UPDATE files fi
                SET ""FolderId"" = (
                    SELECT fo.""Id""
                    FROM folders fo
                    WHERE fo.""Path"" = fi.""Folder""
                );
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "FolderId",
                table: "files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_files_Folder",
                table: "files");

            migrationBuilder.DropColumn(
                name: "Folder",
                table: "files");

            migrationBuilder.CreateIndex(
                name: "IX_files_FolderId",
                table: "files",
                column: "FolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_files_folders_FolderId",
                table: "files",
                column: "FolderId",
                principalTable: "folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_folders_FolderId",
                table: "files");

            migrationBuilder.DropTable(
                name: "folders");

            migrationBuilder.DropIndex(
                name: "IX_files_FolderId",
                table: "files");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "files");

            migrationBuilder.AddColumn<string>(
                name: "Folder",
                table: "files",
                type: "text",
                nullable: false,
                defaultValue: "/");

            migrationBuilder.CreateIndex(
                name: "IX_files_Folder",
                table: "files",
                column: "Folder");
        }
    }
}
