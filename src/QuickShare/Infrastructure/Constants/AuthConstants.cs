namespace QuickShare.Infrastructure.Constants;

public static class AuthConstants
{
    public const string CookieName = ".AspNetCore.QuickShare";

    public const string ProviderClaimType = "provider";

    public const string GoogleProvider = "google";
    public const string GitHubProvider = "github";
    public const string DevProvider = "dev";
    /// <summary>Microsoft Entra ID via OIDC (issue #181). Distinct from consumer Microsoft accounts.</summary>
    public const string EntraProvider = "entra";
    /// <summary>Signed in with a passkey (issue #68). Not a registration provider — an extra door.</summary>
    public const string PasskeyProvider = "passkey";
    /// <summary>Signed in via an emailed link (issue #132).</summary>
    public const string MagicLinkProvider = "magic-link";
}
