namespace QuickShare.Models;

public sealed record UpdateProfileRequest(
    string Name,
    string? AvatarBase64
);
