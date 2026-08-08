namespace QuickShare.Data.Entities;

[DynamicSort]
public record Folder : BaseAuditEntity
{
    public const string RootName = "/";

    public Guid? OwnerId { get; init; }
    public string Name { get; set; } = RootName;
}
