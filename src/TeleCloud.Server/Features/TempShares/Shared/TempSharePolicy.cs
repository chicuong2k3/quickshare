namespace TeleCloud.Server.Features.TempShares.Shared;

/// <summary>
/// Pure expiry decision logic for temp shares. Has no dependencies so it can be unit-tested directly.
/// </summary>
public static class TempSharePolicy
{
    /// <summary>Maximum allowed temp-share upload size (2 GB, inherited from the Telegram backend).</summary>
    public const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Default expiry applied when the request omits the expiry field.</summary>
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    /// <summary>
    /// A share is expired once its <see cref="TempShare.ExpiresAt"/> is at or before <paramref name="now"/>.
    /// The boundary at exactly <c>ExpiresAt</c> counts as expired (hard gate).
    /// </summary>
    public static bool IsExpired(TempShare share, DateTime now)
        => share.ExpiresAt <= now;

    /// <summary>
    /// Parses the <c>expiry</c> form field into a duration. Returns <see langword="null"/> for invalid values.
    /// <c>null</c>/<c>""</c> → default (24h); <c>"24h"</c> → 24h; <c>"7d"</c> → 7d.
    /// </summary>
    public static TimeSpan? TryParseExpiry(string? expiry)
    {
        return expiry switch
        {
            null or "" => DefaultExpiry,
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            _ => null
        };
    }
}
