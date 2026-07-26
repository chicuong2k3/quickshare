namespace QuickShare.Data.Entities;

public sealed record ProfileResponse(
    Guid Id,
    string Name,
    string Email,
    string AvatarUrl,
    string Plan,
    DateTime CreatedAt
);
