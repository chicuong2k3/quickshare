using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workspace_folder_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StructureJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_folder_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_folder_templates_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultExpiryHours = table.Column<double>(type: "double precision", nullable: true),
                    DefaultPasswordRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultPasswordHint = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DefaultPrivacyMode = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_folders_workspace_folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "workspace_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_folders_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_FolderId",
                table: "deliveries",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_folder_templates_WorkspaceId_Name",
                table: "workspace_folder_templates",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_folders_ParentId",
                table: "workspace_folders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_folders_WorkspaceId_ParentId",
                table: "workspace_folders",
                columns: new[] { "WorkspaceId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_folders_WorkspaceId_ParentId_Name",
                table: "workspace_folders",
                columns: new[] { "WorkspaceId", "ParentId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_deliveries_workspace_folders_FolderId",
                table: "deliveries",
                column: "FolderId",
                principalTable: "workspace_folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_deliveries_workspace_folders_FolderId",
                table: "deliveries");

            migrationBuilder.DropTable(
                name: "workspace_folder_templates");

            migrationBuilder.DropTable(
                name: "workspace_folders");

            migrationBuilder.DropIndex(
                name: "IX_deliveries_FolderId",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "deliveries");
        }
    }
}
