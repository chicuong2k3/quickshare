namespace QuickShare.Shared.Models;

[DynamicSort]
public record UserLogin
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ProviderKey { get; init; } = string.Empty;
    public User? User { get; init; }
}
