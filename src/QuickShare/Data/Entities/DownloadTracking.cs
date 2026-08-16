namespace QuickShare.Data.Entities;

[DynamicSort]
public record DownloadTracking
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public DateTime DownloadedAt { get; init; }
    public bool Notified { get; set; }
    /// <summary>Recipient's account id when a signed-in user opts into download history (#220); null for anonymous downloads.</summary>
    public Guid? UserId { get; set; }
}
