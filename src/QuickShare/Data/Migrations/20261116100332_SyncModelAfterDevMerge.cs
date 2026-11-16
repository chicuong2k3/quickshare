using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelAfterDevMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptionPolicy",
                table: "workspaces",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ColdStorageDays",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ColdArchivedBytes",
                table: "user_usage",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ShareCode",
                table: "upload_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalPartsCount",
                table: "upload_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "UploadedBytes",
                table: "upload_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "UploadedPartsCount",
                table: "upload_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "payment_intents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "invoices",
                type: "text",
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

            migrationBuilder.AddColumn<bool>(
                name: "HlsAvailable",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeduplicated",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "OriginalVideoSizeBytes",
                table: "deliveries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestoredUntil",
                table: "deliveries",
                type: "timestamp with time zone",
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

            migrationBuilder.CreateTable(
                name: "automation_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Scopes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenPrefixValue = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WebhookEndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    FromLocalPart = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    VerificationToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpfRecord = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DkimHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    DkimValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DmarcRecord = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SpfVerified = table.Column<bool>(type: "boolean", nullable: false),
                    DkimVerified = table.Column<bool>(type: "boolean", nullable: false),
                    DmarcVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_sending_domains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "deduplicated_blobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StorageRegion = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    RefCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deduplicated_blobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "github_release_repos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WebhookSecret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultExpiryHours = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_release_repos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "penetration_test_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentYear = table.Column<int>(type: "integer", nullable: false),
                    VendorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Scope = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Methodology = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ConductedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriticalCount = table.Column<int>(type: "integer", nullable: false),
                    HighCount = table.Column<int>(type: "integer", nullable: false),
                    MediumCount = table.Column<int>(type: "integer", nullable: false),
                    LowCount = table.Column<int>(type: "integer", nullable: false),
                    RemediatedCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublicSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NdaReportReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_penetration_test_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "support_chat_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SenderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueueTier = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EscalatedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    EscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_chat_threads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_passkey_recoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecoveryCodeHashes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    RequirePasskeyReAdd = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "workspace_byok_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RoleArn = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    KeyRegion = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    LastRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_byok_configs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_sftp_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DefaultFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_sftp_keys", x => x.Id);
                });

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
                name: "IX_deduplicated_blobs_ScopeKey_Sha256_FileSize_StorageRegion",
                table: "deduplicated_blobs",
                columns: new[] { "ScopeKey", "Sha256", "FileSize", "StorageRegion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deduplicated_blobs_StoragePath",
                table: "deduplicated_blobs",
                column: "StoragePath");

            migrationBuilder.CreateIndex(
                name: "IX_github_release_repos_WorkspaceId_RepoFullName",
                table: "github_release_repos",
                columns: new[] { "WorkspaceId", "RepoFullName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_penetration_test_records_AssessmentYear",
                table: "penetration_test_records",
                column: "AssessmentYear",
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
                name: "IX_user_passkey_recoveries_UserId",
                table: "user_passkey_recoveries",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_byok_configs_WorkspaceId",
                table: "workspace_byok_configs",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_sftp_keys_WorkspaceId",
                table: "workspace_sftp_keys",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_sftp_keys_WorkspaceId_Fingerprint",
                table: "workspace_sftp_keys",
                columns: new[] { "WorkspaceId", "Fingerprint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_connections");

            migrationBuilder.DropTable(
                name: "custom_sending_domains");

            migrationBuilder.DropTable(
                name: "deduplicated_blobs");

            migrationBuilder.DropTable(
                name: "github_release_repos");

            migrationBuilder.DropTable(
                name: "penetration_test_records");

            migrationBuilder.DropTable(
                name: "support_chat_messages");

            migrationBuilder.DropTable(
                name: "support_chat_threads");

            migrationBuilder.DropTable(
                name: "user_passkey_recoveries");

            migrationBuilder.DropTable(
                name: "workspace_byok_configs");

            migrationBuilder.DropTable(
                name: "workspace_sftp_keys");

            migrationBuilder.DropColumn(
                name: "EncryptionPolicy",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "ColdStorageDays",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ColdArchivedBytes",
                table: "user_usage");

            migrationBuilder.DropColumn(
                name: "ShareCode",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "TotalPartsCount",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "UploadedBytes",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "UploadedPartsCount",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "invoices");

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
                name: "HlsAvailable",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "IsDeduplicated",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "OriginalVideoSizeBytes",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "RestoredUntil",
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
        }
    }
}
