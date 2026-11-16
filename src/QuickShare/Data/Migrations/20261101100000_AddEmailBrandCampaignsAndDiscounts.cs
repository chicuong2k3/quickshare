using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailBrandCampaignsAndDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "workspace_brands",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "workspace_brands",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoApplyOnCheckout",
                table: "promo_codes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CampaignName",
                table: "promo_codes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "promo_codes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "promo_codes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "discount_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    VerificationEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ProofReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IssuedPromoCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discount_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discount_applications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discount_applications_UserId",
                table: "discount_applications",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discount_applications");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "workspace_brands");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "workspace_brands");

            migrationBuilder.DropColumn(
                name: "AutoApplyOnCheckout",
                table: "promo_codes");

            migrationBuilder.DropColumn(
                name: "CampaignName",
                table: "promo_codes");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "promo_codes");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "promo_codes");
        }
    }
}
