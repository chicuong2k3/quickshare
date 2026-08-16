namespace QuickShare.Data.Entities;

[DynamicSort]
public record Delivery : BaseAuditEntity
{
    public const int MessageMaxLength = 500;
    public const int PasswordMaxLength = 72;

    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; set; }
    public bool SenderEmailConfirmed { get; set; }
    public bool SenderEmailConsent { get; set; }
    public const int FileNameMaxLength = 255;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public long TotalBytes { get; init; }
    public int ChunkCount { get; init; }
    public string ShareCode { get; init; } = string.Empty;
    public const int AliasMinLength = 3;
    public const int AliasMaxLength = 30;

    public int MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    /// <summary>Per-day download cap (issue #227); null = no daily cap. Paid plans only.</summary>
    public int? DailyDownloadLimit { get; set; }
    /// <summary>Memorable slug usable in place of the share code (issue #311); lowercase, unique. Paid plans only.</summary>
    public string? Alias { get; set; }
    /// <summary>Sender-side star (issue #242).</summary>
    public bool IsFavorite { get; set; }
    public const int TagMaxLength = 30;
    /// <summary>Optional sender label (issue #321); color derived from the text.</summary>
    public string? Tag { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? DeliverAt { get; set; }
    public string? OtpHash { get; set; }
    public string? PasswordHash { get; set; }
    public string? Message { get; set; }
    public string? Sha256 { get; set; }
    public DateTime? FirstDownloadedAt { get; set; }
    public bool HideDownloadStats { get; set; }
    public string Status { get; set; } = DeliveryStatus.Active;
    public string Source { get; init; } = DeliverySources.Web;
    public string? StoragePath { get; set; }
    public User? Sender { get; init; }
}

public static class DeliveryStatus
{
    public const string Active = "Active";
    public const string Scheduled = "Scheduled";
    public const string Expired = "Expired";
    public const string Suspended = "Suspended";
    public const string Revoked = "Revoked";
}
