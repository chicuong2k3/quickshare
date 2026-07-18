namespace TeleCloud.Server.Data;

[DynamicSort]
public record Folder
{
    public Guid Id { get; init; }
    public Guid? OwnerId { get; init; }
    public string Path { get; init; } = "/";
    public DateTime CreatedAt { get; init; }
}
