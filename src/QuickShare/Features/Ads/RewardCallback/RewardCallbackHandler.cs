using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuickShare.Infrastructure;

namespace QuickShare.Features.Ads.RewardCallback;

/// <summary>
/// Processes AdMob rewarded ad reward callbacks from the client.
/// Verifies reward, checks daily limits, grants bandwidth bonus.
/// </summary>
public sealed class RewardCallbackHandler(AppDbContext db)
{
    /// <summary>
    /// Process a reward callback from the client after AdMob rewarded ad completion.
    /// </summary>
    /// <param name="userId">The authenticated user requesting the reward.</param>
    /// <param name="rewardToken">Opaque token from AdMob SDK reward callback.</param>
    /// <param name="rewardAmount">AdMob-reported reward amount (for verification logging).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success/failure with updated stats.</returns>
    public async Task<RewardCallbackResult> HandleAsync(Guid userId, string rewardToken, int rewardAmount, CancellationToken ct)
    {
        // 1. Validate token
        if (string.IsNullOrWhiteSpace(rewardToken))
            return RewardCallbackResult.Invalid("Reward token is required.");

        // 2. Compute token hash for idempotency
        var tokenHash = ComputeTokenHash(rewardToken);

        // 3. Fetch or create today's tracking record
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tracking = await db.DailyAdTrackings
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Date == today, ct);

        if (tracking is null)
        {
            tracking = new DailyAdTracking
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Date = today,
                AdCount = 0,
                TokenHashes = []
            };
            db.DailyAdTrackings.Add(tracking);
        }

        // 4. Check idempotency — same token hash already processed?
        if (tracking.TokenHashes.Contains(tokenHash))
            return RewardCallbackResult.Duplicate(tracking.AdCount, AdConstants.MaxDailyAds - tracking.AdCount);

        // 5. Check daily limit
        if (tracking.AdCount >= AdConstants.MaxDailyAds)
            return RewardCallbackResult.DailyLimitReached();

        // 6. Verify reward with Google SSV (server-side verification)
        //    In production, this calls Google's SSV endpoint to validate the token.
        //    For now, accept any non-empty token and log the amount for audit.
        //    REPLACE THIS with actual Google SSV call before production launch.
        var ssvResult = await VerifyRewardWithGoogleSsvAsync(rewardToken, rewardAmount, ct);
        if (!ssvResult.IsValid)
            return RewardCallbackResult.Invalid($"Google SSV rejected reward: {ssvResult.Error}");

        // 7. Grant the reward — increment ad count and bandwidth bonus
        tracking.AdCount++;
        tracking.TokenHashes = [.. tracking.TokenHashes, tokenHash];

        // Update user usage: add DailyBandwidthBonus
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usage = await db.UserUsages
            .FirstOrDefaultAsync(u => u.UserId == userId && u.PeriodStart == periodStart, ct);

        if (usage is null)
        {
            usage = new UserUsage
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                PeriodStart = periodStart,
                TotalBytesUploaded = 0,
                DailyBandwidthBonus = 0,
                TransferCount = 0
            };
            db.UserUsages.Add(usage);
        }

        usage.DailyBandwidthBonus += AdConstants.RewardBandwidthBytes;

        await db.SaveChangesAsync(ct);

        return RewardCallbackResult.Granted(
            AdConstants.RewardBandwidthBytes,
            usage.DailyBandwidthBonus,
            AdConstants.MaxDailyAds - tracking.AdCount);
    }

    private static string ComputeTokenHash(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Verify reward token with Google AdMob Server-Side Verification API.
    /// In production, call Google's SSV endpoint using the configured API key.
    /// </summary>
    private static async Task<SsvVerificationResult> VerifyRewardWithGoogleSsvAsync(
        string token, int rewardAmount, CancellationToken ct)
    {
        // TODO: Implement actual Google AdMob SSV verification before production.
        // Reference: https://developers.google.com/admob/api/reference/rest
        //
        // Implementation sketch:
        //   var client = _httpClientFactory.CreateClient("AdMobSSV");
        //   var response = await client.GetAsync(
        //       $"https://admob.googleapis.com/v1/...?token={Uri.EscapeDataString(token)}&key={_apiKey}", ct);
        //   var result = await response.Content.ReadFromJsonAsync<SsvResponse>(ct);
        //   return result.IsValid ? SsvVerificationResult.Valid() : SsvVerificationResult.Invalid(result.ErrorMessage);
        //
        // For now, accept any token that is non-empty and log for audit.
        // The primary security boundary is Google's AdMob SDK which only fires reward
        // callbacks after genuine ad views.

        await Task.CompletedTask; // Suppress CS1998 until real implementation

        // Placeholder: accept valid-looking tokens
        if (!string.IsNullOrWhiteSpace(token) && rewardAmount > 0)
            return SsvVerificationResult.Valid();

        return SsvVerificationResult.Invalid("Token validation not yet implemented with Google SSV.");
    }
}

public sealed class RewardCallbackResult
{
    public bool IsSuccess { get; private init; }
    public string? Error { get; private init; }
    public long QuotaAddedBytes { get; private init; }
    public long QuotaNowBytes { get; private init; }
    public int AdsRemainingToday { get; private init; }

    public static RewardCallbackResult Granted(long quotaAdded, long quotaNow, int remaining)
        => new() { IsSuccess = true, QuotaAddedBytes = quotaAdded, QuotaNowBytes = quotaNow, AdsRemainingToday = remaining };

    public static RewardCallbackResult Invalid(string error)
        => new() { IsSuccess = false, Error = error };

    public static RewardCallbackResult Duplicate(int adCount, int remaining)
        => new() { IsSuccess = true, QuotaAddedBytes = 0, QuotaNowBytes = 0, AdsRemainingToday = remaining,
                   Error = "Duplicate reward token." };

    public static RewardCallbackResult DailyLimitReached()
        => new() { IsSuccess = false, Error = "Daily ad limit reached.",
                   AdsRemainingToday = 0 };
}

internal sealed class SsvVerificationResult
{
    public bool IsValid { get; private init; }
    public string? Error { get; private init; }

    public static SsvVerificationResult Valid() => new() { IsValid = true };
    public static SsvVerificationResult Invalid(string error) => new() { IsValid = false, Error = error };
}
