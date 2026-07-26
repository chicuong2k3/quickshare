namespace QuickShare.Data.Entities;

[DynamicSort]
public record Delivery : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public long TotalBytes { get; init; }
    public int ChunkCount { get; init; }
    public string ShareCode { get; init; } = string.Empty;
    public int MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    public long EgressBytes { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? PasswordHash { get; set; }
    public string? OtpHash { get; set; }
    public string? BrandLogo { get; set; }
    public string? BrandMessage { get; set; }
    public string? BrandAccent { get; set; }
    public string Status { get; set; } = "Active";
    public string? StoragePath { get; set; }
    public User? Sender { get; init; }
    public Manifest? Manifest { get; init; }
}
