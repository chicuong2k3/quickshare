namespace QuickShare.Infrastructure;

/// <summary>
/// Organizational retention policy (issue #161): an account-wide ceiling on how long
/// any share it creates may live. The sender still picks an expiry per delivery — the
/// policy clamps that choice rather than replacing it, so a shorter pick always wins.
///
/// The window is a whole number of days on the account. Null means no policy, which is
/// the default and the only state a free account can be in.
/// </summary>
public static class RetentionPolicy
{
    public const int MinDays = 1;
    public const int MaxDays = 365;

    /// <summary>The retention windows the settings UI offers. Any value in range is accepted.</summary>
    public static readonly IReadOnlyList<int> Presets = [1, 7, 14, 30, 90];

    /// <summary>True when <paramref name="days"/> is a usable window (null = clearing the policy).</summary>
    public static bool IsValid(int? days) => days is null || (days >= MinDays && days <= MaxDays);

    /// <summary>
    /// Clamps an expiry to the policy window measured from now. Returns
    /// <paramref name="expiresAt"/> unchanged when no policy applies or the expiry
    /// already falls inside the window.
    /// </summary>
    public static DateTime Clamp(DateTime expiresAt, int? retentionDays) =>
        Ceiling(retentionDays) is { } ceiling && expiresAt > ceiling ? ceiling : expiresAt;

    /// <summary>Nullable overload for rows whose expiry is optional; null expiry becomes the ceiling.</summary>
    public static DateTime? Clamp(DateTime? expiresAt, int? retentionDays)
    {
        if (Ceiling(retentionDays) is not { } ceiling)
            return expiresAt;

        return expiresAt is { } value && value <= ceiling ? value : ceiling;
    }

    /// <summary>The latest instant a share may live to under the policy, or null when unpolicied.</summary>
    public static DateTime? Ceiling(int? retentionDays) =>
        retentionDays is { } days && days >= MinDays
            ? DateTime.UtcNow.AddDays(days)
            : null;

    /// <summary>True when the expiry sits beyond the policy window and needs enforcement.</summary>
    public static bool Exceeds(DateTime? expiresAt, int? retentionDays) =>
        Ceiling(retentionDays) is { } ceiling && (expiresAt is null || expiresAt > ceiling);
}
