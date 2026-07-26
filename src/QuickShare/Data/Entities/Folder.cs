namespace QuickShare.Data.Entities;

[DynamicSort]
public record Folder : BaseAuditEntity
{
    public Guid? OwnerId { get; init; }
    public string Name { get; init; } = "/";
}
