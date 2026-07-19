using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    ShareCode = table.Column<string>(type: "text", nullable: false),
                    MaxDownloads = table.Column<int>(type: "integer", nullable: false),
                    DownloadCount = table.Column<int>(type: "integer", nullable: false),
                    EgressBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    BrandLogo = table.Column<string>(type: "text", nullable: true),
                    BrandMessage = table.Column<string>(type: "text", nullable: true),
                    BrandAccent = table.Column<string>(type: "text", nullable: true),
                    Price = table.Column<decimal>(type: "numeric", nullable: true),
                    PaymentStatus = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliveries_users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "download_tracking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_tracking", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    StorageRef = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chunks_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedData = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_manifests_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chunks_DeliveryId_Index",
                table: "chunks",
                columns: new[] { "DeliveryId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_ExpiresAt",
                table: "deliveries",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_SenderId",
                table: "deliveries",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_ShareCode",
                table: "deliveries",
                column: "ShareCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_tracking_DeliveryId",
                table: "download_tracking",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_download_tracking_DownloadedAt",
                table: "download_tracking",
                column: "DownloadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_manifests_DeliveryId",
                table: "manifests",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_ExpiresAt",
                table: "upload_sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_SenderId",
                table: "upload_sessions",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chunks");

            migrationBuilder.DropTable(
                name: "download_tracking");

            migrationBuilder.DropTable(
                name: "manifests");

            migrationBuilder.DropTable(
                name: "upload_sessions");

            migrationBuilder.DropTable(
                name: "deliveries");
        }
    }
}
