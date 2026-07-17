namespace TeleCloud.Server.Data;

[DynamicSort]
public record TempShare
{
    public Guid Id { get; init; }
    public Guid FileId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public FileMetadata? File { get; init; }
}
