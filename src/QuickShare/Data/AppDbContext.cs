
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
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<DailyAdTracking> DailyAdTrackings => Set<DailyAdTracking>();
    public DbSet<DailyP2PUsage> DailyP2PUsages => Set<DailyP2PUsage>();

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
            entity.HasOne<Delivery>()
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.BillingPeriod).IsRequired();
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

    }
}
