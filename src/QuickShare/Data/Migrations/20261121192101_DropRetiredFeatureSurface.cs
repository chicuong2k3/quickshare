using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropRetiredFeatureSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A share must never become *less* protected because a control was retired.
            // Every row that depended on a dropped protection — a sender-set code, an IP or
            // country rule, or a passphrase-encrypted key — is expired first, so the
            // recipient gets an honest "this link has ended" instead of the file. The
            // sender re-shares, and nothing silently loosens.
            migrationBuilder.Sql("""
                UPDATE deliveries
                SET "ExpiresAt" = NOW()
                WHERE "DeletedAt" IS NULL
                  AND "ExpiresAt" > NOW()
                  AND (
                        ("OtpHash" IS NOT NULL AND "PasswordHash" IS NULL AND NOT "EmailOtpEnabled")
                     OR (("AllowedIpRanges" IS NOT NULL OR "AllowedCountries" IS NOT NULL OR "BlockedCountries" IS NOT NULL)
                          AND "PasswordHash" IS NULL AND NOT "EmailOtpEnabled")
                     OR "EncryptionMode" = 'passphrase'
                  );
                """);

            migrationBuilder.DropColumn(
                name: "EncryptionPolicy",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "AllowedCountries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "AllowedIpRanges",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "BlockedCountries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EncryptedKeyEscrow",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EncryptionKdfIterations",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EncryptionKdfSalt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "EncryptionMode",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "OtpHash",
                table: "deliveries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptionPolicy",
                table: "workspaces",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AllowedCountries",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedIpRanges",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedCountries",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedKeyEscrow",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EncryptionKdfIterations",
                table: "deliveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKdfSalt",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionMode",
                table: "deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OtpHash",
                table: "deliveries",
                type: "text",
                nullable: true);
        }
    }
}
