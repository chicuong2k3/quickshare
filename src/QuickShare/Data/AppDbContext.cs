
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
