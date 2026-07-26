namespace QuickShare.Data.Entities;

public sealed record RegisterRequest(
    string Provider,
    string ProviderKey,
    string Name,
    string Email,
    string? AvatarUrl
);
