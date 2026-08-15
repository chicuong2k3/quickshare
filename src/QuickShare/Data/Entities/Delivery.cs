namespace QuickShare.Data.Entities;

[DynamicSort]
public record Delivery : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; set; }
    public bool SenderEmailConfirmed { get; set; }
    public bool SenderEmailConsent { get; set; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public long TotalBytes { get; init; }
    public int ChunkCount { get; init; }
    public string ShareCode { get; init; } = string.Empty;
    public int MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? OtpHash { get; set; }
    public string Status { get; set; } = DeliveryStatus.Active;
    public string Source { get; init; } = DeliverySources.Web;
    public string? StoragePath { get; set; }
    public User? Sender { get; init; }
}

public static class DeliveryStatus
{
    public const string Active = "Active";
    public const string Expired = "Expired";
}
