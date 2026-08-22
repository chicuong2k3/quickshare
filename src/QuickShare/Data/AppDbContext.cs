
namespace QuickShare.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<UserUsage> UserUsages => Set<UserUsage>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<FileRequest> FileRequests => Set<FileRequest>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<DownloadTracking> DownloadTrackings => Set<DownloadTracking>();
    public DbSet<OtpLockout> OtpLockouts => Set<OtpLockout>();
    public DbSet<AbuseReport> AbuseReports => Set<AbuseReport>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<ReferralEvent> ReferralEvents => Set<ReferralEvent>();
    public DbSet<TempUpload> TempUploads => Set<TempUpload>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<DailyAdTracking> DailyAdTrackings => Set<DailyAdTracking>();
    public DbSet<DailyP2PUsage> DailyP2PUsages => Set<DailyP2PUsage>();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => Set<FeatureFlagOverride>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ExtensionRequest> ExtensionRequests => Set<ExtensionRequest>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<RecipientContact> RecipientContacts => Set<RecipientContact>();
    public DbSet<FileLock> FileLocks => Set<FileLock>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<WorkspaceTransfer> WorkspaceTransfers => Set<WorkspaceTransfer>();
    public DbSet<WorkspaceFolder> WorkspaceFolders => Set<WorkspaceFolder>();
    public DbSet<WorkspaceFolderTemplate> WorkspaceFolderTemplates => Set<WorkspaceFolderTemplate>();
    public DbSet<BillingProfile> BillingProfiles => Set<BillingProfile>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<SiemSink> SiemSinks => Set<SiemSink>();
    public DbSet<DpaSignature> DpaSignatures => Set<DpaSignature>();
    public DbSet<WorkspaceBackupPolicy> WorkspaceBackupPolicies => Set<WorkspaceBackupPolicy>();
    public DbSet<WorkspaceBrand> WorkspaceBrands => Set<WorkspaceBrand>();
    public DbSet<RecurringDelivery> RecurringDeliveries => Set<RecurringDelivery>();
    public DbSet<DeliveryComment> DeliveryComments => Set<DeliveryComment>();
    public DbSet<WorkspaceChannelHook> WorkspaceChannelHooks => Set<WorkspaceChannelHook>();
    public DbSet<WorkspaceDomain> WorkspaceDomains => Set<WorkspaceDomain>();
    public DbSet<WorkspaceS3Credential> WorkspaceS3Credentials => Set<WorkspaceS3Credential>();
    public DbSet<UserTotp> UserTotps => Set<UserTotp>();
    public DbSet<WorkspaceSsoConfig> WorkspaceSsoConfigs => Set<WorkspaceSsoConfig>();
    public DbSet<WorkspaceScimToken> WorkspaceScimTokens => Set<WorkspaceScimToken>();
    public DbSet<SamlAssertionSeen> SamlAssertionsSeen => Set<SamlAssertionSeen>();
    public DbSet<DeliveryFile> DeliveryFiles => Set<DeliveryFile>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserPasskey> UserPasskeys => Set<UserPasskey>();
    public DbSet<PasskeyChallenge> PasskeyChallenges => Set<PasskeyChallenge>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoRedemption> PromoRedemptions => Set<PromoRedemption>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.Entity<UserLogin>(entity =>
        {
            entity.ToTable("user_logins");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Provider, e.ProviderKey }).IsUnique();
            entity.Property(e => e.Provider).IsRequired();
            entity.Property(e => e.ProviderKey).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserUsage>(entity =>
        {
            entity.ToTable("user_usage");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.PeriodStart }).IsUnique();
            entity.Property(e => e.PeriodStart).IsRequired();
            entity.Property(e => e.TotalBytesUploaded).IsRequired();
            entity.Property(e => e.TransferCount).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.ToTable("deliveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired();
            entity.Property(e => e.ContentType).IsRequired();
            entity.Property(e => e.ShareCode).IsRequired();
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.ShareCode).IsUnique();
            entity.Property(e => e.Alias).HasMaxLength(Delivery.AliasMaxLength);
            entity.HasIndex(e => e.Alias).IsUnique();
            entity.Property(e => e.AllowedIpRanges).HasMaxLength(Delivery.AccessRuleMaxLength);
            entity.Property(e => e.AllowedCountries).HasMaxLength(Delivery.AccessRuleMaxLength);
            entity.Property(e => e.BlockedCountries).HasMaxLength(Delivery.AccessRuleMaxLength);
            entity.Property(e => e.LegalHoldReason).HasMaxLength(Delivery.LegalHoldReasonMaxLength);
            // The retention sweep and every purge path filter on the hold flag.
            entity.HasIndex(e => e.LegalHoldAt);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(e => e.Sender)
                .WithMany()
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.WorkspaceId);
            entity.Property(e => e.StorageRegion).HasMaxLength(StorageRegionOption.CodeMaxLength);
            entity.Property(e => e.DenyReason).HasMaxLength(Delivery.MessageMaxLength);
            entity.Property(e => e.PendingRecipients).HasMaxLength(RecurringDelivery.RecipientsMaxLength);
            entity.HasIndex(e => e.CommentsEnabled);
            entity.HasIndex(e => e.FolderId);
            // Restrict, not cascade: deleting a folder must never take its deliveries with it.
            // The folder delete handler refuses while anything is still filed under it.
            entity.HasOne<WorkspaceFolder>()
                .WithMany()
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.FileRequestId);
            entity.HasOne(e => e.FileRequest)
                .WithMany(e => e.Deliveries)
                .HasForeignKey(e => e.FileRequestId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FileRequest>(entity =>
        {
            entity.ToTable("file_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(FileRequest.CodeMaxLength);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(FileRequest.TitleMaxLength);
            entity.Property(e => e.Message).HasMaxLength(FileRequest.MessageMaxLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(16);
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UploadSession>(entity =>
        {
            entity.ToTable("upload_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.SenderId);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.FileRequestId);
            entity.HasOne<FileRequest>()
                .WithMany()
                .HasForeignKey(e => e.FileRequestId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DownloadTracking>(entity =>
        {
            entity.ToTable("download_tracking");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeliveryId);
            entity.HasIndex(e => e.DownloadedAt);
            entity.HasIndex(e => e.Notified);
            entity.HasIndex(e => e.UserId);
            entity.HasOne<Delivery>()
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OtpLockout>(entity =>
        {
            entity.ToTable("otp_lockouts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeliveryId).IsUnique();
            entity.HasOne<Delivery>()
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AbuseReport>(entity =>
        {
            entity.ToTable("abuse_reports");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeliveryId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Category).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.HasOne<Delivery>()
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserDevice>(entity =>
        {
            entity.ToTable("user_devices");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.FingerprintHash }).IsUnique();
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReferralEvent>(entity =>
        {
            entity.ToTable("referral_events");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ReferrerUserId);
            entity.HasIndex(e => e.ReferredUserId).IsUnique();
            entity.Property(e => e.Type).IsRequired();
        });

        modelBuilder.Entity<TempUpload>(entity =>
        {
            entity.ToTable("temp_uploads");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.FileName).IsRequired();
            entity.Property(e => e.TempPath).IsRequired();
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.ToTable("payment_events");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt).IsDescending();
            entity.Property(e => e.EventType).IsRequired();
            entity.Property(e => e.Plan).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Plan).IsRequired();
            entity.Property(e => e.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<SubscriptionStatus>(v, ignoreCase: true))
                .IsRequired();
            entity.Property(e => e.BillingPeriod)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<BillingPeriod>(v, ignoreCase: true))
                .IsRequired();
            entity.Property(e => e.CurrentPeriodStart).IsRequired();
            entity.Property(e => e.CurrentPeriodEnd).IsRequired();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CurrentPeriodEnd);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentIntent>(entity =>
        {
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(16);
            entity.Property(e => e.ProviderChargeId).HasMaxLength(64);
            // The webhook arrives knowing only the charge id, so that is the lookup.
            entity.HasIndex(e => e.ProviderChargeId);
            entity.ToTable("payment_intents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.PayOSOrderCode).IsRequired();
            entity.HasIndex(e => e.PayOSOrderCode).IsUnique();
            entity.Property(e => e.Plan).IsRequired();
            entity.Property(e => e.Billing).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DailyAdTracking>(entity =>
        {
            entity.ToTable("daily_ad_tracking");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Date }).IsUnique();
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.AdCount).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.TokenHashes).HasColumnType("text[]").HasDefaultValue(Array.Empty<string>());
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DailyP2PUsage>(entity =>
        {
            entity.ToTable("daily_p2p_usage");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(64);
            entity.Property(e => e.UploadCount).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.UnlockedUploads).IsRequired().HasDefaultValue(0);
            // Authenticated: one row per user per day
            entity.HasIndex(e => new { e.UserId, e.Date })
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL");
            // Anonymous: one row per IP per day
            entity.HasIndex(e => new { e.IpAddress, e.Date })
                .IsUnique()
                .HasFilter("\"UserId\" IS NULL");
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FeatureFlagOverride>(entity =>
        {
            entity.ToTable("feature_flag_overrides");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.ToTable("analytics_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventName).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CountryCode).HasMaxLength(2);
            entity.HasIndex(e => new { e.EventName, e.CreatedAt });
            entity.HasIndex(e => new { e.VisitorId, e.CreatedAt });
            entity.HasIndex(e => e.CountryCode);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(Notification.TypeMaxLength);
            entity.Property(e => e.Title).HasMaxLength(Notification.TitleMaxLength);
            entity.Property(e => e.LinkUrl).HasMaxLength(Notification.LinkMaxLength);
            entity.HasIndex(e => new { e.UserId, e.IsRead });
            entity.HasIndex(e => e.CreatedAt).IsDescending();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExtensionRequest>(entity =>
        {
            entity.ToTable("extension_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequesterToken).IsRequired().HasMaxLength(ExtensionRequest.RequesterTokenMaxLength);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(16);
            entity.Property(e => e.RejectMessage).HasMaxLength(ExtensionRequest.RejectMessageMaxLength);
            entity.HasIndex(e => new { e.DeliveryId, e.RequesterToken });
            entity.HasIndex(e => new { e.DeliveryId, e.Status });
            entity.HasOne(e => e.Delivery)
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Endpoint).IsRequired().HasMaxLength(PushSubscription.EndpointMaxLength);
            entity.Property(e => e.P256dh).IsRequired().HasMaxLength(PushSubscription.KeyMaxLength);
            entity.Property(e => e.Auth).IsRequired().HasMaxLength(PushSubscription.KeyMaxLength);
            entity.Property(e => e.DeviceName).HasMaxLength(PushSubscription.DeviceNameMaxLength);
            entity.HasIndex(e => e.Endpoint).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.ToTable("security_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<SecurityEventType>(v, ignoreCase: true))
                .IsRequired();
            entity.Property(e => e.IpHash).IsRequired();
            entity.Property(e => e.UserAgentLabel).IsRequired();
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.EventType);
            // The workspace audit view (issue #145) reads this store filtered by workspace.
            entity.HasIndex(e => new { e.WorkspaceId, e.CreatedAt });
            // Shadow navigation — the record intentionally carries no User reference.
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecipientContact>(entity =>
        {
            entity.ToTable("recipient_contacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(254);
            entity.HasIndex(e => new { e.UserId, e.Email }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.LastUsedAt });
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileLock>(entity =>
        {
            entity.ToTable("file_locks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasMaxLength(FileLock.ReasonMaxLength);
            entity.HasIndex(e => new { e.DeliveryId, e.ReleasedAt });
            entity.HasOne<Delivery>()
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.LockedById)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(Workspace.NameMaxLength);
            entity.HasIndex(e => e.OwnerId);
            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.ToTable("workspace_members");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InviteEmail).IsRequired().HasMaxLength(WorkspaceMember.EmailMaxLength);
            entity.Property(e => e.InviteTokenHash).HasMaxLength(WorkspaceMember.TokenHashLength);
            entity.Property(e => e.Role)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<WorkspaceRole>(v, ignoreCase: true))
                .IsRequired();
            entity.HasIndex(e => new { e.WorkspaceId, e.RevokedAt });
            // One live seat per account across all workspaces — the plan override on
            // User.Plan has exactly one source, so overlapping seats cannot fight over it.
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL AND \"RevokedAt\" IS NULL");
            entity.HasIndex(e => e.InviteTokenHash);
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryComment>(entity =>
        {
            entity.ToTable("delivery_comments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Body).IsRequired().HasMaxLength(DeliveryComment.BodyMaxLength);
            entity.Property(e => e.AuthorName).IsRequired().HasMaxLength(DeliveryComment.AuthorNameMaxLength);
            entity.Property(e => e.AuthorIpHash).HasMaxLength(64);
            // The thread view reads a delivery in creation order; the rate check reads by author.
            entity.HasIndex(e => new { e.DeliveryId, e.CreatedAt });
            entity.HasIndex(e => new { e.AuthorIpHash, e.CreatedAt });
            entity.HasOne(e => e.Delivery)
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
            // A reply belongs to its parent; deleting the parent row would orphan it, but the
            // delete path is soft precisely so that never happens.
            entity.HasOne<DeliveryComment>()
                .WithMany()
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserTotp>(entity =>
        {
            // One enrolment per account: a second authenticator would be a second thing to
            // revoke on offboarding, and nobody asked for one.
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.Secret).IsRequired().HasMaxLength(UserTotp.SecretLength);
            entity.Property(e => e.RecoveryCodeHashes).IsRequired().HasMaxLength(UserTotp.RecoveryCodesMaxLength);
        });

        modelBuilder.Entity<WorkspaceSsoConfig>(entity =>
        {
            entity.HasIndex(e => e.WorkspaceId).IsUnique();
            // Sign-in looks the directory up by claim, so this is the hot path, not WorkspaceId.
            entity.HasIndex(e => new { e.Provider, e.DirectoryId });
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(32);
            entity.Property(e => e.DirectoryId).IsRequired().HasMaxLength(WorkspaceSsoConfig.DirectoryIdMaxLength);
            entity.Property(e => e.AllowedEmailDomains).HasMaxLength(WorkspaceSsoConfig.DomainsMaxLength);
            entity.Property(e => e.AdminGroupId).HasMaxLength(WorkspaceSsoConfig.GroupIdMaxLength);
            entity.Property(e => e.SamlSsoUrl).HasMaxLength(WorkspaceSsoConfig.SamlUrlMaxLength);
            entity.Property(e => e.SamlCertificate).HasMaxLength(WorkspaceSsoConfig.SamlCertificateMaxLength);
        });

        modelBuilder.Entity<PromoCode>(entity =>
        {
            entity.ToTable("promo_codes");
            // Codes are looked up by their text on every checkout, and must be unique.
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(PromoCode.CodeMaxLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Description).HasMaxLength(PromoCode.DescriptionMaxLength);
            entity.Property(e => e.AppliesToPlans).HasMaxLength(64);
        });

        modelBuilder.Entity<PromoRedemption>(entity =>
        {
            entity.ToTable("promo_redemptions");
            entity.HasKey(e => e.Id);
            // The enforcement of "one per customer", not a report of it: without the unique
            // index, two concurrent requests both pass the check and both redeem.
            entity.HasIndex(e => new { e.PromoCodeId, e.UserId }).IsUnique();
            entity.HasOne(e => e.PromoCode).WithMany().HasForeignKey(e => e.PromoCodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPasskey>(entity =>
        {
            entity.ToTable("user_passkeys");
            // Sign-in resolves an account from a credential id, so this is the hot path.
            entity.HasIndex(e => e.CredentialId).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.CredentialId).IsRequired().HasMaxLength(UserPasskey.CredentialIdMaxLength);
            entity.Property(e => e.PublicKey).IsRequired().HasMaxLength(UserPasskey.PublicKeyMaxLength);
            entity.Property(e => e.Name).HasMaxLength(UserPasskey.NameMaxLength);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasskeyChallenge>(entity =>
        {
            entity.ToTable("passkey_challenges");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Handle).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Handle).IsRequired().HasMaxLength(PasskeyChallenge.HandleLength);
            entity.Property(e => e.OptionsJson).IsRequired().HasMaxLength(PasskeyChallenge.OptionsMaxLength);
        });

        modelBuilder.Entity<LoginToken>(entity =>
        {
            entity.ToTable("login_tokens");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(LoginToken.EmailMaxLength);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(LoginToken.TokenHashLength);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(e => e.Id);
            // Read on every authenticated request — this index is the feature's whole cost model.
            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.LastSeenAt });
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(UserSession.SessionIdLength);
            entity.Property(e => e.UserAgentLabel).IsRequired().HasMaxLength(UserSession.LabelMaxLength);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("api_keys");
            // Authentication looks a key up by hash on every API call, so this is the hot path.
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.KeyHash).IsRequired().HasMaxLength(ApiKey.HashLength);
            entity.Property(e => e.Prefix).IsRequired().HasMaxLength(ApiKey.PrefixLength);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(ApiKey.NameMaxLength);
            entity.Property(e => e.Scopes).IsRequired().HasMaxLength(64);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookEndpoint>(entity =>
        {
            entity.ToTable("webhook_endpoints");
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(WebhookEndpoint.UrlMaxLength);
            entity.Property(e => e.Secret).IsRequired().HasMaxLength(WebhookEndpoint.SecretLength);
            entity.Property(e => e.Events).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(WebhookEndpoint.DescriptionMaxLength);
            entity.Property(e => e.LastError).HasMaxLength(WebhookEndpoint.ErrorMaxLength);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.ToTable("webhook_deliveries");
            entity.HasKey(e => e.Id);
            // The job's query: pending rows that are due, oldest first.
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            // The log's query: this account's attempts, newest first.
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(16);
            entity.Property(e => e.PayloadJson).IsRequired().HasMaxLength(WebhookDelivery.PayloadMaxLength);
            entity.Property(e => e.ResponseBody).HasMaxLength(WebhookDelivery.ResponseMaxLength);
            entity.HasOne(e => e.Endpoint).WithMany().HasForeignKey(e => e.EndpointId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryFile>(entity =>
        {
            entity.ToTable("delivery_files");
            entity.HasKey(e => e.Id);
            // Listing a bundle is always "every file of one delivery, in the sender's order".
            entity.HasIndex(e => new { e.DeliveryId, e.SortOrder });
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(DeliveryFile.FileNameMaxLength);
            entity.Property(e => e.ContentType).HasMaxLength(DeliveryFile.ContentTypeMaxLength);
            entity.Property(e => e.Sha256).HasMaxLength(DeliveryFile.Sha256Length);
            entity.Property(e => e.ObjectKey).IsRequired();
            // The rows are the delivery's contents; they have no meaning without it.
            entity.HasOne(e => e.Delivery)
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SamlAssertionSeen>(entity =>
        {
            // The unique index is the replay check itself, not an optimisation for it.
            entity.HasIndex(e => e.AssertionId).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.AssertionId).IsRequired().HasMaxLength(SamlAssertionSeen.AssertionIdMaxLength);
        });

        modelBuilder.Entity<WorkspaceScimToken>(entity =>
        {
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.WorkspaceId);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(WorkspaceScimToken.TokenHashLength);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(WorkspaceScimToken.LabelMaxLength);
        });

        modelBuilder.Entity<WorkspaceChannelHook>(entity =>
        {
            entity.ToTable("workspace_channel_hooks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(16);
            entity.Property(e => e.WebhookUrl).IsRequired().HasMaxLength(WorkspaceChannelHook.UrlMaxLength);
            entity.Property(e => e.LastError).HasMaxLength(WorkspaceChannelHook.ErrorMaxLength);
            // One channel per workspace. Fan-out to several is a router's job, as with the SIEM sink.
            entity.HasIndex(e => e.WorkspaceId).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringDelivery>(entity =>
        {
            entity.ToTable("recurring_deliveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(RecurringDelivery.NameMaxLength);
            entity.Property(e => e.Frequency).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Recipients).HasMaxLength(RecurringDelivery.RecipientsMaxLength);
            entity.Property(e => e.LastError).HasMaxLength(RecurringDelivery.ErrorMaxLength);
            // The job scans by due time, so that is the index that matters.
            entity.HasIndex(e => new { e.Enabled, e.NextRunAt });
            entity.HasIndex(e => e.OwnerId);
            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict, not cascade: deleting the source delivery should surface as a stopped
            // schedule the owner can see, not a schedule that silently disappears.
            entity.HasOne<Delivery>()
                .WithMany()
                .HasForeignKey(e => e.SourceDeliveryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkspaceS3Credential>(entity =>
        {
            entity.ToTable("workspace_s3_credentials");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(WorkspaceS3Credential.LabelMaxLength);
            entity.Property(e => e.AccessKeyId).IsRequired().HasMaxLength(WorkspaceS3Credential.AccessKeyIdLength);
            entity.Property(e => e.SecretProtected).IsRequired().HasMaxLength(WorkspaceS3Credential.ProtectedMaxLength);
            entity.Property(e => e.Scopes).IsRequired().HasMaxLength(64);
            // The access key is what a signature arrives with, so it is the lookup and it is unique.
            entity.HasIndex(e => e.AccessKeyId).IsUnique();
            entity.HasIndex(e => e.WorkspaceId);
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceDomain>(entity =>
        {
            entity.ToTable("workspace_domains");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Host).IsRequired().HasMaxLength(WorkspaceDomain.HostMaxLength);
            entity.Property(e => e.VerificationToken).IsRequired().HasMaxLength(WorkspaceDomain.TokenLength);
            entity.Property(e => e.LastError).HasMaxLength(WorkspaceDomain.ErrorMaxLength);
            // Unique on the host, not just per workspace: two workspaces claiming one hostname is
            // the same hole the SSO directory had — whoever registered first would absorb the
            // other's recipients.
            entity.HasIndex(e => e.Host).IsUnique();
            // One domain per workspace. A second address for the same brand is a redirect.
            entity.HasIndex(e => e.WorkspaceId).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceBrand>(entity =>
        {
            entity.ToTable("workspace_brands");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AccentColor).HasMaxLength(WorkspaceBrand.ColorMaxLength);
            entity.Property(e => e.AccentTextColor).HasMaxLength(WorkspaceBrand.ColorMaxLength);
            entity.Property(e => e.HeaderText).HasMaxLength(WorkspaceBrand.HeaderMaxLength);
            entity.Property(e => e.FooterText).HasMaxLength(WorkspaceBrand.FooterMaxLength);
            entity.Property(e => e.HeadingFontName).HasMaxLength(WorkspaceBrand.FontNameMaxLength);
            entity.Property(e => e.BodyFontName).HasMaxLength(WorkspaceBrand.FontNameMaxLength);
            entity.Property(e => e.FontLicenseNote).HasMaxLength(WorkspaceBrand.LicenseNoteMaxLength);
            entity.Property(e => e.LogoKey).HasMaxLength(WorkspaceBrand.AssetKeyMaxLength);
            entity.Property(e => e.HeadingFontKey).HasMaxLength(WorkspaceBrand.AssetKeyMaxLength);
            entity.Property(e => e.BodyFontKey).HasMaxLength(WorkspaceBrand.AssetKeyMaxLength);
            entity.HasIndex(e => e.WorkspaceId).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceBackupPolicy>(entity =>
        {
            entity.ToTable("workspace_backup_policies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Frequency).IsRequired().HasMaxLength(16);
            entity.Property(e => e.LastStatus).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Endpoint).HasMaxLength(WorkspaceBackupPolicy.EndpointMaxLength);
            entity.Property(e => e.Region).HasMaxLength(64);
            entity.Property(e => e.BucketName).HasMaxLength(WorkspaceBackupPolicy.BucketMaxLength);
            entity.Property(e => e.KeyPrefix).HasMaxLength(WorkspaceBackupPolicy.PrefixMaxLength);
            entity.Property(e => e.AccessKeyId).HasMaxLength(WorkspaceBackupPolicy.SecretMaxLength);
            entity.Property(e => e.AccessKeySecret).HasMaxLength(WorkspaceBackupPolicy.SecretMaxLength);
            entity.Property(e => e.LastError).HasMaxLength(WorkspaceBackupPolicy.ErrorMaxLength);
            entity.Property(e => e.LastManifestKey).HasMaxLength(WorkspaceBackupPolicy.KeyMaxLength);
            // One policy per workspace. Several destinations is a replication problem, and
            // solving it here would mean owning partial-failure semantics across them.
            entity.HasIndex(e => e.WorkspaceId).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiemSink>(entity =>
        {
            entity.ToTable("siem_sinks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Endpoint).IsRequired().HasMaxLength(SiemSink.EndpointMaxLength);
            entity.Property(e => e.Format).IsRequired().HasMaxLength(SiemSink.FormatMaxLength);
            entity.Property(e => e.Secret).HasMaxLength(SiemSink.SecretMaxLength);
            entity.Property(e => e.EventFilter).HasMaxLength(SiemSink.EventFilterMaxLength);
            entity.Property(e => e.LastError).HasMaxLength(SiemSink.ErrorMaxLength);
            // One sink per workspace. Fan-out to several SIEMs is a router's job, not ours.
            entity.HasIndex(e => e.WorkspaceId).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DpaSignature>(entity =>
        {
            entity.ToTable("dpa_signatures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Version).IsRequired().HasMaxLength(DpaSignature.VersionMaxLength);
            entity.Property(e => e.CompanyName).IsRequired().HasMaxLength(DpaSignature.CompanyNameMaxLength);
            entity.Property(e => e.SignerName).IsRequired().HasMaxLength(DpaSignature.SignerMaxLength);
            entity.Property(e => e.SignerEmail).IsRequired().HasMaxLength(DpaSignature.SignerMaxLength);
            // One signature per account per version; re-signing the same terms is a no-op,
            // and a new version is a new row rather than an edit to the old evidence.
            entity.HasIndex(e => new { e.UserId, e.Version }).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillingProfile>(entity =>
        {
            entity.ToTable("billing_profiles");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.CompanyName).HasMaxLength(BillingProfile.CompanyNameMaxLength);
            entity.Property(e => e.TaxCode).HasMaxLength(BillingProfile.TaxCodeMaxLength);
            entity.Property(e => e.Address).HasMaxLength(BillingProfile.AddressMaxLength);
            entity.Property(e => e.BillingEmail).HasMaxLength(BillingProfile.CompanyNameMaxLength);
            entity.Property(e => e.PoNumber).HasMaxLength(BillingProfile.PoNumberMaxLength);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Number).HasMaxLength(Invoice.NumberMaxLength);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(Invoice.StatusMaxLength);
            entity.Property(e => e.CompanyName).HasMaxLength(BillingProfile.CompanyNameMaxLength);
            entity.Property(e => e.TaxCode).HasMaxLength(BillingProfile.TaxCodeMaxLength);
            entity.Property(e => e.Address).HasMaxLength(BillingProfile.AddressMaxLength);
            entity.Property(e => e.PoNumber).HasMaxLength(BillingProfile.PoNumberMaxLength);
            // An invoice number is a legal identifier — two rows sharing one is a filing error.
            entity.HasIndex(e => e.Number)
                .IsUnique()
                .HasFilter("\"Number\" <> ''");
            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.HasIndex(e => e.DueAt);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.ToTable("invoice_lines");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(InvoiceLine.KindMaxLength);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(InvoiceLine.DescriptionMaxLength);
            entity.HasIndex(e => e.InvoiceId);
            entity.HasOne(e => e.Invoice)
                .WithMany(e => e.Lines)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceFolder>(entity =>
        {
            entity.ToTable("workspace_folders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(WorkspaceFolder.NameMaxLength);
            entity.Property(e => e.DefaultPasswordHint).HasMaxLength(WorkspaceFolder.HintMaxLength);
            entity.HasIndex(e => new { e.WorkspaceId, e.ParentId });
            // Sibling names stay unique so a template applied twice cannot quietly produce
            // two folders the team cannot tell apart.
            entity.HasIndex(e => new { e.WorkspaceId, e.ParentId, e.Name }).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            // Deleting a parent must not cascade away a child that still holds deliveries —
            // the delete handler refuses a non-empty folder instead.
            entity.HasOne<WorkspaceFolder>()
                .WithMany()
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkspaceFolderTemplate>(entity =>
        {
            entity.ToTable("workspace_folder_templates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(WorkspaceFolderTemplate.NameMaxLength);
            entity.Property(e => e.StructureJson).IsRequired().HasMaxLength(WorkspaceFolderTemplate.StructureMaxLength);
            entity.HasIndex(e => new { e.WorkspaceId, e.Name }).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceTransfer>(entity =>
        {
            entity.ToTable("workspace_transfers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(WorkspaceMember.TokenHashLength);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => new { e.WorkspaceId, e.AcceptedAt });
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
