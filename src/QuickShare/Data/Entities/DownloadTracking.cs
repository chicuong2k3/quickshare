namespace QuickShare.Data.Entities;

[DynamicSort]
public record DownloadTracking
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public DateTime DownloadedAt { get; init; }
}
