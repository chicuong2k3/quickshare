namespace TeleCloud.Server.Data;

public class TeleCloudDbContext : DbContext
{
    public TeleCloudDbContext(DbContextOptions<TeleCloudDbContext> options) : base(options) { }

    public DbSet<FileMetadata> Files => Set<FileMetadata>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<TempShare> TempShares => Set<TempShare>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<UserUsage> UserUsages => Set<UserUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Path).IsUnique();
            entity.HasIndex(e => e.OwnerId);
            entity.Property(e => e.Path).IsRequired();
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

        modelBuilder.Entity<TempShare>(entity =>
        {
            entity.ToTable("temp_shares");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileId).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.OwnerId);
            entity.HasOne(e => e.File!)
                .WithMany()
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
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
    }
}
