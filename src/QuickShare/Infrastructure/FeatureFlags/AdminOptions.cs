namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>
/// Config-based admin list. A request is treated as admin when its email claim is in
/// <see cref="Emails"/>. Kept minimal and separate from a user model so the admin
/// surface can exist before a real admin dashboard (issue #61).
/// </summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>Emails allowed to use the admin surface (feature-flag toggles).</summary>
    public string[] Emails { get; set; } = [];
}