namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>
/// Stable wire-format keys for every feature flag. These strings are persisted in
/// the <c>feature_flag_overrides</c> table and referenced in appsettings.json, so they
/// are frozen once shipped — rename = data migration.
/// </summary>
public static class FeatureFlagKeys
{
    /// <summary>File previews on the download page (image/PDF/video). Issue #21.</summary>
    public const string Previews = "previews";

    /// <summary>True zero-knowledge mode: client-side AES-GCM with key in the link fragment. Issue #26.</summary>
    public const string ZeroKnowledge = "zero-knowledge";

    /// <summary>Password-protected downloads (share password in addition to OTP gate). Issue #44.</summary>
    public const string PasswordProtection = "password-protection";

    /// <summary>Virus scanning for Pro/Studio uploads. Issue #48.</summary>
    public const string VirusScan = "virus-scan";
}