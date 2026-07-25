namespace QuickShare.Shared;

public static class AuthConstants
{
    public const string CookieName = ".AspNetCore.QuickShare";

    // Claim type constants
    public const string ProviderClaimType = "provider";
    public const string AvatarUrlClaimType = "avatar_url";

    // Auth provider identifiers
    public const string GoogleProvider = "google";
    public const string DevProvider = "dev";
}
