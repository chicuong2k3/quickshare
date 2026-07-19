namespace QuickShare.Server.Features.Users.Profile;

public sealed record UserProfileResponse(
    Guid Id,
    string Name,
    string Email,
    string AvatarUrl,
    string Plan,
    DateTime CreatedAt
);
