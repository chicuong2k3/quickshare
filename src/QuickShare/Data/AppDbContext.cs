
namespace QuickShare.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<UserUsage> UserUsages => Set<UserUsage>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
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
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(e => e.Sender)
                .WithMany()
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
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

    }
}
