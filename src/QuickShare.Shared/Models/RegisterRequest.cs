namespace QuickShare.Shared.Models;

public sealed record RegisterRequest(
    string Provider,
    string ProviderKey,
    string Name,
    string Email,
    string? AvatarUrl
);
