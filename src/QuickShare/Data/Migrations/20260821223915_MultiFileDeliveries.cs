using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiFileDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileCount",
                table: "deliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TotalFileBytes",
                table: "deliveries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "delivery_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ObjectKey = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_files_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_files_DeliveryId_SortOrder",
                table: "delivery_files",
                columns: new[] { "DeliveryId", "SortOrder" });

            // Backfill: every delivery that predates multi-file becomes a one-file bundle.
            //
            // This is what makes "every delivery has at least one delivery_files row" true
            // without exception, which is the invariant the whole feature rests on. Without it,
            // every reader would need a fallback to the deliveries columns when the list came
            // back empty — and the ones that forgot would show an empty file list for the
            // majority of deliveries in the system.
            //
            // Postgres-specific (gen_random_uuid). That is fine: Postgres is what this runs on,
            // and the test suite builds its schema with EnsureCreated rather than migrations.
            migrationBuilder.Sql(@"
                INSERT INTO delivery_files (""Id"", ""DeliveryId"", ""FileName"", ""ContentType"", ""FileSize"", ""ObjectKey"", ""Sha256"", ""SortOrder"", ""CreatedAt"")
                SELECT gen_random_uuid(), d.""Id"", d.""FileName"",
                       COALESCE(d.""ContentType"", 'application/octet-stream'),
                       d.""FileSize"", COALESCE(d.""StoragePath"", ''), d.""Sha256"", 0, d.""CreatedAt""
                FROM deliveries d
                WHERE d.""StoragePath"" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM delivery_files f WHERE f.""DeliveryId"" = d.""Id"");");

            // TotalFileBytes starts life equal to the single file it describes.
            migrationBuilder.Sql(@"UPDATE deliveries SET ""TotalFileBytes"" = ""FileSize"" WHERE ""TotalFileBytes"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_files");

            migrationBuilder.DropColumn(
                name: "FileCount",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "TotalFileBytes",
                table: "deliveries");
        }
    }
}
