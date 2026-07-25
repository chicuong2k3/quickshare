namespace QuickShare.Shared.Models;

[DynamicSort]
public record TempShare : BaseAuditEntity
{
    public Guid FileId { get; init; }
    public Guid? OwnerId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public FileMetadata? File { get; init; }
    public User? Owner { get; init; }
}
