namespace QuickShare.Data.Entities;

public sealed record UpdateProfileRequest(
    string Name,
    string? AvatarBase64
);
