using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentsTransferCreditsAffiliatesSmsAndMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "WorkspaceSsoConfigs");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "WorkspaceSsoConfigs");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "WorkspaceScimTokens");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "WorkspaceScimTokens");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "workspace_s3_credentials");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "workspace_s3_credentials");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "workspace_channel_hooks");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "workspace_channel_hooks");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "workspace_backup_policies");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "workspace_backup_policies");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "user_consents");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "user_consents");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "siem_sinks");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "siem_sinks");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "recurring_deliveries");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "recurring_deliveries");

            migrationBuilder.DropColumn(
                name: "EmailReplyTo",
                table: "feature_flag_overrides");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "feature_flag_overrides");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "TargetCountry",
                table: "api_keys");

            migrationBuilder.AddColumn<string>(
                name: "TransferPassToken",
                table: "upload_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DunningAttemptCount",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DunningGraceEndsAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDunningAttemptAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateSnapshot",
                table: "payment_intents",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "affiliate_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommissionPercent = table.Column<int>(type: "integer", nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayoutDetails = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TermsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    ClicksCount = table.Column<long>(type: "bigint", nullable: false),
                    SignupsCount = table.Column<long>(type: "bigint", nullable: false),
                    PaidConversionsCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalEarnedVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    PendingPayoutVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "delivery_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_versions_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Surface = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VariantACopy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    VariantBCopy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    TrafficSplitPercent = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    WinnerVariant = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "migration_import_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConflictPolicy = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SummaryJson = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_import_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sms_delivery_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    MessageBody = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CostVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    PhoneNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    RecipientEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Billing = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "transfer_credit_passes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreditsRemaining = table.Column<int>(type: "integer", nullable: false),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxExpiryDays = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_credit_passes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "affiliate_conversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferredUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SaleAmountVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    CommissionAmountVnd = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "IX_delivery_versions_DeliveryId_VersionNumber",
                table: "delivery_versions",
                columns: new[] { "DeliveryId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_experiments_Key",
                table: "experiments",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_migration_import_jobs_UserId",
                table: "migration_import_jobs",
                column: "UserId");

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
                name: "IX_transfer_credit_passes_PaymentIntentId",
                table: "transfer_credit_passes",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_credit_passes_Token",
                table: "transfer_credit_passes",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affiliate_conversions");

            migrationBuilder.DropTable(
                name: "delivery_versions");

            migrationBuilder.DropTable(
                name: "experiments");

            migrationBuilder.DropTable(
                name: "migration_import_jobs");

            migrationBuilder.DropTable(
                name: "sms_delivery_logs");

            migrationBuilder.DropTable(
                name: "sms_opt_outs");

            migrationBuilder.DropTable(
                name: "subscription_gifts");

            migrationBuilder.DropTable(
                name: "transfer_credit_passes");

            migrationBuilder.DropTable(
                name: "affiliate_profiles");

            migrationBuilder.DropColumn(
                name: "TransferPassToken",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "DunningAttemptCount",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "DunningGraceEndsAt",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "LastDunningAttemptAt",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSnapshot",
                table: "payment_intents");

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "WorkspaceSsoConfigs",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "WorkspaceSsoConfigs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "WorkspaceScimTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "WorkspaceScimTokens",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "workspace_s3_credentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "workspace_s3_credentials",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "workspace_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "workspace_members",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "workspace_channel_hooks",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "workspace_channel_hooks",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "workspace_backup_policies",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "workspace_backup_policies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "webhook_endpoints",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "webhook_endpoints",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "webhook_endpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "webhook_endpoints",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "user_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "user_sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "user_consents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "user_consents",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "siem_sinks",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "siem_sinks",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "recurring_deliveries",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "recurring_deliveries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailReplyTo",
                table: "feature_flag_overrides",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "feature_flag_overrides",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "api_keys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCountry",
                table: "api_keys",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }
    }
}
