namespace QuickShare.Models;

[DynamicSort]
public record FileMetadata : BaseAuditEntity
{
    public string Filename { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
    public long ChannelId { get; init; }
    public int MessageId { get; init; }
    public long AccessHash { get; init; }
    public byte[] FileReference { get; init; } = [];
    public long DocumentId { get; init; }
    public Guid FolderId { get; init; }
    public Folder? Folder { get; init; }
    public Guid? OwnerId { get; init; }
    public string[] Tags { get; init; } = [];
}
