namespace QuickShare.Models;

[DynamicSort]
public record BaseAuditEntity
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
