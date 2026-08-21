using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentsAndChannelHooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommentPolicy",
                table: "workspaces",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "CommentsEnabled",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "delivery_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AuthorIpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_comments_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delivery_comments_delivery_comments_ParentId",
                        column: x => x.ParentId,
                        principalTable: "delivery_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_channel_hooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnComment = table.Column<bool>(type: "boolean", nullable: false),
                    LastPostedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_channel_hooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_channel_hooks_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_CommentsEnabled",
                table: "deliveries",
                column: "CommentsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_comments_AuthorIpHash_CreatedAt",
                table: "delivery_comments",
                columns: new[] { "AuthorIpHash", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_comments_DeliveryId_CreatedAt",
                table: "delivery_comments",
                columns: new[] { "DeliveryId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_comments_ParentId",
                table: "delivery_comments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_channel_hooks_WorkspaceId",
                table: "workspace_channel_hooks",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_comments");

            migrationBuilder.DropTable(
                name: "workspace_channel_hooks");

            migrationBuilder.DropIndex(
                name: "IX_deliveries_CommentsEnabled",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "CommentPolicy",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "CommentsEnabled",
                table: "deliveries");
        }
    }
}
