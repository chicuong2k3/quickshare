using System.Security.Cryptography;
using System.Text;

namespace QuickShare.Features.Users.Shared;

/// <summary>
/// Referral program (issue #15): unique short code per account, one Pending
/// event per referred signup, +2GB for BOTH sides released only on the
/// referred user's first upload (anti-abuse: a signup that never uploads
/// rewards nobody). Self-referrals are ignored.
/// </summary>
public static class ReferralService
{
    public static async Task<string> GenerateCodeAsync(AppDbContext db, CancellationToken ct)
    {
        while (true)
        {
            var code = BuildCode();
            if (!await db.Users.AnyAsync(u => u.ReferralCode == code, ct))
                return code;
        }
    }

    /// <summary>Links a fresh account to the referrer from the qs_ref cookie.</summary>
    public static async Task ClaimAsync(AppDbContext db, Guid referredUserId, string? referralCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referralCode))
            return;

        var referrer = await db.Users.FirstOrDefaultAsync(u => u.ReferralCode == referralCode, ct);
        if (referrer is null || referrer.Id == referredUserId)
            return;

        var referredUser = await db.Users.FindAsync([referredUserId], ct);
        if (referredUser is null)
            return;

        referredUser.ReferredByUserId = referrer.Id;
        db.ReferralEvents.Add(new ReferralEvent
        {
            Id = Guid.CreateVersion7(),
            ReferrerUserId = referrer.Id,
            ReferredUserId = referredUserId,
            Type = ReferralEventTypes.Pending
        });
    }

    /// <summary>
    /// Releases the +2GB bonus on the referred user's first upload. The caller
    /// persists via its own SaveChanges.
    /// </summary>
    public static async Task ReleaseOnFirstUploadAsync(User user, AppDbContext db, CancellationToken ct)
    {
        if (user.ReferredByUserId is null || user.ReferredByUserId.Value == user.Id)
            return;

        var pending = await db.ReferralEvents
            .FirstOrDefaultAsync(e => e.ReferredUserId == user.Id && e.Type == ReferralEventTypes.Pending, ct);
        if (pending is null)
            return;

        var referrer = await db.Users.FindAsync([pending.ReferrerUserId], ct);
        if (referrer is null)
        {
            pending.Type = ReferralEventTypes.Unrewarded;
            return;
        }

        var now = DateTime.UtcNow;
        await db.Users
            .Where(u => u.Id == referrer.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.BonusQuotaBytes,
                    Math.Min(referrer.BonusQuotaBytes + ReferralPolicy.BonusQuotaBytes, ReferralPolicy.MaxBonusQuotaBytes))
                .SetProperty(u => u.UpdatedAt, now), ct);

        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.BonusQuotaBytes,
                    Math.Min(user.BonusQuotaBytes + ReferralPolicy.BonusQuotaBytes, ReferralPolicy.MaxBonusQuotaBytes))
                .SetProperty(u => u.UpdatedAt, now), ct);

        pending.Type = ReferralEventTypes.Rewarded;
        pending.UpdatedAt = now;
    }

    private static string BuildCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(ReferralPolicy.ReferralCodeLength);
        var sb = new StringBuilder(ReferralPolicy.ReferralCodeLength);
        foreach (var b in bytes)
            sb.Append(alphabet[b % alphabet.Length]);

        return sb.ToString();
    }
}
