namespace QuickShare.Shared.Models;

[DynamicSort]
public record Manifest
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public byte[] EncryptedData { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Delivery? Delivery { get; init; }
}
