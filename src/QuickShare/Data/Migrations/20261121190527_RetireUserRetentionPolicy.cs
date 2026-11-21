using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetireUserRetentionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affiliate_conversions");

            migrationBuilder.DropTable(
                name: "automation_connections");

            migrationBuilder.DropTable(
                name: "custom_sending_domains");

            migrationBuilder.DropTable(
                name: "delivery_ocr_texts");

            migrationBuilder.DropTable(
                name: "discount_applications");

            migrationBuilder.DropTable(
                name: "dpa_signatures");

            migrationBuilder.DropTable(
                name: "experiments");

            migrationBuilder.DropTable(
                name: "file_locks");

            migrationBuilder.DropTable(
                name: "github_release_repos");

            migrationBuilder.DropTable(
                name: "login_tokens");

            migrationBuilder.DropTable(
                name: "migration_import_jobs");

            migrationBuilder.DropTable(
                name: "passkey_challenges");

            migrationBuilder.DropTable(
                name: "penetration_test_records");

            migrationBuilder.DropTable(
                name: "recurring_deliveries");

            migrationBuilder.DropTable(
                name: "SamlAssertionsSeen");

            migrationBuilder.DropTable(
                name: "siem_sinks");

            migrationBuilder.DropTable(
                name: "sms_delivery_logs");

            migrationBuilder.DropTable(
                name: "sms_opt_outs");

            migrationBuilder.DropTable(
                name: "subscription_gifts");

            migrationBuilder.DropTable(
                name: "support_chat_messages");

            migrationBuilder.DropTable(
                name: "support_chat_threads");

            migrationBuilder.DropTable(
                name: "transfer_credit_passes");

            migrationBuilder.DropTable(
                name: "user_passkey_recoveries");

            migrationBuilder.DropTable(
                name: "user_passkeys");

            migrationBuilder.DropTable(
                name: "UserTotps");

            migrationBuilder.DropTable(
                name: "workspace_backup_policies");

            migrationBuilder.DropTable(
                name: "workspace_byok_configs");

            migrationBuilder.DropTable(
                name: "workspace_s3_credentials");

            migrationBuilder.DropTable(
                name: "workspace_sftp_keys");

            migrationBuilder.DropTable(
                name: "WorkspaceScimTokens");

            migrationBuilder.DropTable(
                name: "WorkspaceSsoConfigs");

            migrationBuilder.DropTable(
                name: "affiliate_profiles");

            migrationBuilder.DropIndex(
                name: "IX_payment_intents_ProviderChargeId",
                table: "payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_invoices_DueAt",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_deliveries_LegalHoldAt",
                table: "deliveries");

            migrationBuilder.DropIndex(
                name: "IX_deduplicated_blobs_ScopeKey_Sha256_FileSize_StorageRegion",
                table: "deduplicated_blobs");

            migrationBuilder.DropColumn(
                name: "AllowSelfApproval",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "MfaGraceDays",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "MfaRequired",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "MfaRequiredAt",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "RequireDeliveryApproval",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "DefaultRequireApproval",
                table: "workspace_folders");

            migrationBuilder.DropColumn(
                name: "PreferredStorageRegion",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RetentionDays",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RetentionNoExtend",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ColdArchivedBytes",
                table: "user_usage");

            migrationBuilder.DropColumn(
                name: "TransferPassToken",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "ProviderChargeId",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "NetTermsDays",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PoNumber",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "ApprovalRequestedAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ByokKeyId",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ByokKeyVersion",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ByokWrappedDek",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "DenyReason",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "HlsAvailable",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "LegalHoldAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "LegalHoldById",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "LegalHoldReason",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "OcrRequested",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "OriginalVideoSizeBytes",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "PendingRecipients",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "RestoredUntil",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "StorageRegion",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "StorageTier",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "VideoCompressionQuality",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "VirusScanStatus",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "VirusScannedAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "VirusSignature",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "StorageRegion",
                table: "deduplicated_blobs");

            migrationBuilder.DropColumn(
                name: "NetTermsApprovedAt",
                table: "billing_profiles");

            migrationBuilder.DropColumn(
                name: "NetTermsApprovedById",
                table: "billing_profiles");

            migrationBuilder.DropColumn(
                name: "NetTermsDays",
                table: "billing_profiles");

            migrationBuilder.DropColumn(
                name: "PoNumber",
                table: "billing_profiles");

            migrationBuilder.CreateIndex(
                name: "IX_deduplicated_blobs_ScopeKey_Sha256_FileSize",
                table: "deduplicated_blobs",
                columns: new[] { "ScopeKey", "Sha256", "FileSize" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_deduplicated_blobs_ScopeKey_Sha256_FileSize",
                table: "deduplicated_blobs");

            migrationBuilder.AddColumn<bool>(
                name: "AllowSelfApproval",
                table: "workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MfaGraceDays",
                table: "workspaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MfaRequired",
                table: "workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaRequiredAt",
                table: "workspaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireDeliveryApproval",
                table: "workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultRequireApproval",
                table: "workspace_folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreferredStorageRegion",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetentionDays",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RetentionNoExtend",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ColdArchivedBytes",
                table: "user_usage",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "TransferPassToken",
                table: "upload_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "payment_intents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderChargeId",
                table: "payment_intents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetTermsDays",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PoNumber",
                table: "invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalRequestedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ByokKeyId",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ByokKeyVersion",
                table: "deliveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ByokWrappedDek",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenyReason",
                table: "deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HlsAvailable",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalHoldAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalHoldById",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "deliveries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OcrRequested",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "OriginalVideoSizeBytes",
                table: "deliveries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingRecipients",
                table: "deliveries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestoredUntil",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageRegion",
                table: "deliveries",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageTier",
                table: "deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VideoCompressionQuality",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VirusScanStatus",
                table: "deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "VirusScannedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VirusSignature",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageRegion",
                table: "deduplicated_blobs",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NetTermsApprovedAt",
                table: "billing_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NetTermsApprovedById",
                table: "billing_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetTermsDays",
                table: "billing_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PoNumber",
                table: "billing_profiles",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "affiliate_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClicksCount = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommissionPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidConversionsCount = table.Column<long>(type: "bigint", nullable: false),
                    PayoutDetails = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PendingPayoutVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    SignupsCount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TermsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    TotalEarnedVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_affiliate_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_affiliate_profiles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automation_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenPrefixValue = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookEndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_sending_domains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DkimHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    DkimValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DkimVerified = table.Column<bool>(type: "boolean", nullable: false),
                    DmarcRecord = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DmarcVerified = table.Column<bool>(type: "boolean", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    FromLocalPart = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    SpfRecord = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SpfVerified = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_sending_domains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_ocr_texts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_ocr_texts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_ocr_texts_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discount_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedPromoCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProofReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "dpa_signatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpHash = table.Column<string>(type: "text", nullable: false),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dpa_signatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dpa_signatures_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Surface = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TrafficSplitPercent = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VariantACopy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    VariantBCopy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    WinnerVariant = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "file_locks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_locks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_locks_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_file_locks_users_LockedById",
                        column: x => x.LockedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "github_release_repos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultExpiryHours = table.Column<int>(type: "integer", nullable: false),
                    RepoFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_release_repos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "login_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "migration_import_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConflictPolicy = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    SourceProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SummaryJson = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_import_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "passkey_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Handle = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    OptionsJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkey_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "penetration_test_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentYear = table.Column<int>(type: "integer", nullable: false),
                    ConductedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriticalCount = table.Column<int>(type: "integer", nullable: false),
                    HighCount = table.Column<int>(type: "integer", nullable: false),
                    LowCount = table.Column<int>(type: "integer", nullable: false),
                    MediumCount = table.Column<int>(type: "integer", nullable: false),
                    Methodology = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NdaReportReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    NextScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublicSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RemediatedCount = table.Column<int>(type: "integer", nullable: false),
                    RetestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scope = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VendorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_penetration_test_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HourUtc = table.Column<int>(type: "integer", nullable: false),
                    LastDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Recipients = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RunCount = table.Column<int>(type: "integer", nullable: false),
                    SourceDeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurring_deliveries_deliveries_SourceDeliveryId",
                        column: x => x.SourceDeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_deliveries_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SamlAssertionsSeen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssertionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SamlAssertionsSeen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "siem_sinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EventFilter = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    LastShippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastShippedEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siem_sinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siem_sinks_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sms_delivery_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CostVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageBody = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_delivery_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sms_opt_outs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_opt_outs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_gifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Billing = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_gifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscription_gifts_users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_chat_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SenderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SenderRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_chat_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "support_chat_threads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscalatedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueueTier = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TicketNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_chat_threads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_credit_passes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreditsRemaining = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxExpiryDays = table.Column<int>(type: "integer", nullable: false),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RedeemedDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_credit_passes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_passkey_recoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveryCodeHashes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    RequirePasskeyReAdd = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_passkey_recoveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_passkey_recoveries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_passkeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AaGuid = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CredentialId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_passkeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_passkeys_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTotps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedStep = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryCodeHashes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Secret = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTotps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTotps_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_backup_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessKeyId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccessKeySecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BucketName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastDeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    LastManifestBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastManifestKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RetainedCopies = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_backup_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_backup_policies_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_byok_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    KeyRegion = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    LastRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RoleArn = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_byok_configs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_s3_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessKeyId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretProtected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_s3_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_s3_credentials_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workspace_s3_credentials_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_sftp_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_sftp_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceScimTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceScimTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceScimTokens_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceSsoConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AllowedEmailDomains = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DirectoryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    JitRole = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequireSso = table.Column<bool>(type: "boolean", nullable: false),
                    SamlCertificate = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    SamlSsoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSsoConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceSsoConfigs_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "affiliate_conversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionAmountVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReferredUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleAmountVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_affiliate_conversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_affiliate_conversions_affiliate_profiles_AffiliateProfileId",
                        column: x => x.AffiliateProfileId,
                        principalTable: "affiliate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_ProviderChargeId",
                table: "payment_intents",
                column: "ProviderChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_DueAt",
                table: "invoices",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_LegalHoldAt",
                table: "deliveries",
                column: "LegalHoldAt");

            migrationBuilder.CreateIndex(
                name: "IX_deduplicated_blobs_ScopeKey_Sha256_FileSize_StorageRegion",
                table: "deduplicated_blobs",
                columns: new[] { "ScopeKey", "Sha256", "FileSize", "StorageRegion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_affiliate_conversions_AffiliateProfileId_ReferredUserId",
                table: "affiliate_conversions",
                columns: new[] { "AffiliateProfileId", "ReferredUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_affiliate_profiles_Code",
                table: "affiliate_profiles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_affiliate_profiles_UserId",
                table: "affiliate_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automation_connections_TokenHash",
                table: "automation_connections",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automation_connections_UserId",
                table: "automation_connections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_sending_domains_Domain",
                table: "custom_sending_domains",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_sending_domains_UserId",
                table: "custom_sending_domains",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_ocr_texts_DeliveryId",
                table: "delivery_ocr_texts",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discount_applications_UserId",
                table: "discount_applications",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dpa_signatures_UserId_Version",
                table: "dpa_signatures",
                columns: new[] { "UserId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_experiments_Key",
                table: "experiments",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_locks_DeliveryId_ReleasedAt",
                table: "file_locks",
                columns: new[] { "DeliveryId", "ReleasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_file_locks_LockedById",
                table: "file_locks",
                column: "LockedById");

            migrationBuilder.CreateIndex(
                name: "IX_github_release_repos_WorkspaceId_RepoFullName",
                table: "github_release_repos",
                columns: new[] { "WorkspaceId", "RepoFullName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_login_tokens_ExpiresAt",
                table: "login_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_login_tokens_TokenHash",
                table: "login_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_migration_import_jobs_UserId",
                table: "migration_import_jobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_passkey_challenges_ExpiresAt",
                table: "passkey_challenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_passkey_challenges_Handle",
                table: "passkey_challenges",
                column: "Handle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_penetration_test_records_AssessmentYear",
                table: "penetration_test_records",
                column: "AssessmentYear",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_deliveries_Enabled_NextRunAt",
                table: "recurring_deliveries",
                columns: new[] { "Enabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_deliveries_OwnerId",
                table: "recurring_deliveries",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_deliveries_SourceDeliveryId",
                table: "recurring_deliveries",
                column: "SourceDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_SamlAssertionsSeen_AssertionId",
                table: "SamlAssertionsSeen",
                column: "AssertionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SamlAssertionsSeen_ExpiresAt",
                table: "SamlAssertionsSeen",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_siem_sinks_WorkspaceId",
                table: "siem_sinks",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sms_delivery_logs_DeliveryId",
                table: "sms_delivery_logs",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_sms_delivery_logs_SenderUserId",
                table: "sms_delivery_logs",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sms_opt_outs_PhoneNumber",
                table: "sms_opt_outs",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_gifts_PaymentIntentId",
                table: "subscription_gifts",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_gifts_SenderUserId",
                table: "subscription_gifts",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_gifts_Token",
                table: "subscription_gifts",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_chat_messages_ThreadId",
                table: "support_chat_messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_support_chat_threads_TicketNumber",
                table: "support_chat_threads",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_chat_threads_UserId",
                table: "support_chat_threads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_credit_passes_PaymentIntentId",
                table: "transfer_credit_passes",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_credit_passes_Token",
                table: "transfer_credit_passes",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_passkey_recoveries_UserId",
                table: "user_passkey_recoveries",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_passkeys_CredentialId",
                table: "user_passkeys",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_passkeys_UserId",
                table: "user_passkeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTotps_UserId",
                table: "UserTotps",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_backup_policies_WorkspaceId",
                table: "workspace_backup_policies",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_byok_configs_WorkspaceId",
                table: "workspace_byok_configs",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_s3_credentials_AccessKeyId",
                table: "workspace_s3_credentials",
                column: "AccessKeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_s3_credentials_UserId",
                table: "workspace_s3_credentials",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_s3_credentials_WorkspaceId",
                table: "workspace_s3_credentials",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_sftp_keys_WorkspaceId",
                table: "workspace_sftp_keys",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_sftp_keys_WorkspaceId_Fingerprint",
                table: "workspace_sftp_keys",
                columns: new[] { "WorkspaceId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceScimTokens_TokenHash",
                table: "WorkspaceScimTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceScimTokens_WorkspaceId",
                table: "WorkspaceScimTokens",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSsoConfigs_Provider_DirectoryId",
                table: "WorkspaceSsoConfigs",
                columns: new[] { "Provider", "DirectoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSsoConfigs_WorkspaceId",
                table: "WorkspaceSsoConfigs",
                column: "WorkspaceId",
                unique: true);
        }
    }
}
