namespace QuickShare.Data.Entities;

[DynamicSort]
public record BaseAuditEntity : ICursorKeyed
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
