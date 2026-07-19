namespace QuickShare.Server.Data;

[DynamicSort]
public record Chunk
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public int Index { get; init; }
    public long Size { get; init; }
    public string StorageRef { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Delivery? Delivery { get; init; }
}
