namespace TeleCloud.Server.Data;

public class TeleCloudDbContext : DbContext
{
    public TeleCloudDbContext(DbContextOptions<TeleCloudDbContext> options) : base(options) { }

    public DbSet<FileMetadata> Files => Set<FileMetadata>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<TempShare> TempShares => Set<TempShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Path).IsUnique();
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
            entity.HasOne(e => e.File!)
                .WithMany()
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
