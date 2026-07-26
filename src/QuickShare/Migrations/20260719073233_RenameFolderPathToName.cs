using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Migrations
{
    /// <inheritdoc />
    public partial class RenameFolderPathToName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Path",
                table: "folders",
                newName: "Name");

            migrationBuilder.RenameIndex(
                name: "IX_folders_Path",
                table: "folders",
                newName: "IX_folders_Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "folders",
                newName: "Path");

            migrationBuilder.RenameIndex(
                name: "IX_folders_Name",
                table: "folders",
                newName: "IX_folders_Path");
        }
    }
}
