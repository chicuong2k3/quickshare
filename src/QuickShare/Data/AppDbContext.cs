
namespace QuickShare.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FileMetadata> Files => Set<FileMetadata>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<TempShare> TempShares => Set<TempShare>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<UserUsage> UserUsages => Set<UserUsage>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Manifest> Manifests => Set<Manifest>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<DownloadTracking> DownloadTrackings => Set<DownloadTracking>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.OwnerId);
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.Entity<FileMetadata>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Filename).IsRequired();
            entity.Property(e => e.ContentType).IsRequired();
            entity.Property(e => e.FileReference).IsRequired();
            entity.HasIndex(e => e.FolderId);
            entity.HasIndex(e => e.Filename);
            entity.HasIndex(e => e.CreatedAt).IsDescending();
            entity.HasIndex(e => e.OwnerId);
            entity.HasOne(e => e.Folder)
                .WithMany()
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

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

        modelBuilder.Entity<Manifest>(entity =>
        {
            entity.ToTable("manifests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EncryptedData).IsRequired();
            entity.HasOne(e => e.Delivery)
                .WithOne(d => d.Manifest)
                .HasForeignKey<Manifest>(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Chunk>(entity =>
        {
            entity.ToTable("chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StorageRef).IsRequired();
            entity.Property(e => e.Sha256).IsRequired();
            entity.HasIndex(e => new { e.DeliveryId, e.Index }).IsUnique();
            entity.HasOne(e => e.Delivery)
                .WithMany()
                .HasForeignKey(e => e.DeliveryId)
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

    }
}
