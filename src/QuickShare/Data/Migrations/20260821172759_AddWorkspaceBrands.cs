using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace_brands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LogoKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    AccentTextColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    HeaderText = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FooterText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HideQuickShareBranding = table.Column<bool>(type: "boolean", nullable: false),
                    HeadingFontKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    BodyFontKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    HeadingFontName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BodyFontName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    FontLicenseNote = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_brands_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_brands_WorkspaceId",
                table: "workspace_brands",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_brands");
        }
    }
}
