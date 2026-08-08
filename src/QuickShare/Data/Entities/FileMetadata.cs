namespace QuickShare.Data.Entities;

[DynamicSort]
public record FileMetadata : BaseAuditEntity
{
    public string Filename { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
    public Guid FolderId { get; set; }
    public Folder? Folder { get; init; }
    public Guid? OwnerId { get; init; }
}
