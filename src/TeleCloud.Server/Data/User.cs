namespace TeleCloud.Server.Data;

public record User
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
