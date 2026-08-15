using System.Security.Cryptography;
using System.Text;

namespace QuickShare.Features.Users.Shared;

/// <summary>
/// Device fingerprint + first-seen tracking for the "new sign-in" alert
/// (issue #149). The fingerprint is a SHA-256 of UA + IP — raw values are
/// never stored (privacy #26). Returns whether the login came from a device
/// this account has never seen before.
/// </summary>
public sealed class DeviceTrackerService
{
    public static string ComputeFingerprint(string userAgent, string ip) =>
        Sha256Hex(string.Join('|', userAgent, ip));

    public static string HashIp(string ip) => Sha256Hex(ip);

    /// <summary>
    /// Records a login against the account's known devices. Returns the device
    /// row (new or known) plus a flag marking a first-ever sighting.
    /// </summary>
    public async Task<(UserDevice Device, bool IsNew)> RecordLoginAsync(
        AppDbContext db, Guid userId, string userAgent, string ip, CancellationToken ct)
    {
        var fingerprintHash = ComputeFingerprint(userAgent, ip);
        var now = DateTime.UtcNow;

        var existing = await db.UserDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.FingerprintHash == fingerprintHash, ct);

        if (existing is not null)
        {
            existing.LastSeenAt = now;
            existing.IpHash = HashIp(ip);
            await db.SaveChangesAsync(ct);
            return (existing, false);
        }

        var device = new UserDevice
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FingerprintHash = fingerprintHash,
            UserAgentLabel = SummarizeUserAgent(userAgent),
            IpHash = HashIp(ip),
            FirstSeenAt = now,
            LastSeenAt = now
        };
        db.UserDevices.Add(device);
        await db.SaveChangesAsync(ct);
        return (device, true);
    }

    /// <summary>Short human label from a UA string, e.g. "Chrome / Windows".</summary>
    public static string SummarizeUserAgent(string userAgent)
    {
        var browser = "Browser";
        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) browser = "Edge";
        else if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) browser = "Firefox";
        else if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)) browser = "Chrome";
        else if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)) browser = "Safari";

        var os = "Unknown OS";
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) os = "Windows";
        else if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)) os = "Android";
        else if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) os = "iOS";
        else if (userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase)) os = "macOS";
        else if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) os = "Linux";

        return $"{browser} / {os}";
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }
}
