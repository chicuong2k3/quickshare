namespace QuickShare.Server.Features.Users.Profile;

public sealed record UpdateUserProfileRequest(string Name, string? AvatarBase64);
