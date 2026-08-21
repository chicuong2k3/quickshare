namespace QuickShare.Data.Entities;

/// <summary>
/// An in-app notification (issue #246). Language-neutral: the page maps <see cref="Type"/>
/// to a localized message template and interpolates <see cref="Title"/> (e.g. a file name).
/// </summary>
[DynamicSort]
public record Notification : BaseAuditEntity
{
    public const int TypeMaxLength = 32;
    public const int TitleMaxLength = 200;
    public const int LinkMaxLength = 500;

    public Guid UserId { get; init; }
    public string Type { get; init; } = NotificationType.Info;
    /// <summary>Free-text context (usually a file name) interpolated into the localized message.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>Where the notification links through to (e.g. /d/{code}); null = no link.</summary>
    public string? LinkUrl { get; init; }
    public bool IsRead { get; set; }

    public User? User { get; init; }
}

public static class NotificationType
{
    public const string Info = "info";
    public const string DeliveryExpired = "delivery_expired";
    public const string DeliveryDownloaded = "delivery_downloaded";
    public const string ExtensionRequested = "extension_requested";
    public const string FileLockBroken = "file_lock_broken";
    /// <summary>Monthly quota usage crossed 80/100/120% (issue #141).</summary>
    public const string QuotaThreshold = "quota_threshold";
    /// <summary>A net-terms invoice passed its due date (issue #192).</summary>
    public const string InvoiceOverdue = "invoice_overdue";
}
