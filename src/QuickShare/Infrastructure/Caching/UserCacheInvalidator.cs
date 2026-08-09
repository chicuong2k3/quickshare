using Microsoft.EntityFrameworkCore;

namespace QuickShare.Infrastructure.Caching;

/// <summary>
/// Evicts cache entries that hold user-derived data (profile, avatar URL, plan,
/// billing history). MUST be called after any mutation of User / PaymentEvent /
/// Subscription rows: GetUserAsync caches the whole User entity for 5 minutes and
/// BillingHistoryService caches the full response for 2 minutes. Without eviction,
/// profile saves and completed payments keep showing stale avatars / Free plans /
/// empty payment history until the TTL expires.
/// </summary>
public static class UserCacheInvalidator
{
    /// <summary>Key format used by <see cref="QuickShare.Extensions.ClaimsPrincipalExtensions.GetUserAsync"/>.</summary>
    public const string UserLoginKeyPrefix = "user:login:";
    public const string BillingHistoryKeyPrefix = "billing:history:";
    public const string UserPlanKeyPrefix = "user:plan:";

    public static string UserLoginKey(string provider, string providerKey) =>
        $"{UserLoginKeyPrefix}{provider}:{providerKey}";

    public static string BillingHistoryKey(Guid userId) =>
        $"{BillingHistoryKeyPrefix}{userId}";

    /// <summary>
    /// Removes every cache entry derived from a user (all their login provider
    /// entries plus the billing-history snapshot). Reads logins from the DB, so it
    /// works from any mutation site that only knows the user id (webhooks, hosted
    /// services), not just from code holding a ClaimsPrincipal.
    /// </summary>
    public static async ValueTask EvictAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var cache = StaticCache.Instance;

        var logins = await db.UserLogins
            .AsNoTracking()
            .Where(ul => ul.UserId == userId)
            .Select(ul => new { ul.Provider, ul.ProviderKey })
            .ToListAsync(ct);

        foreach (var login in logins)
        {
            await cache.RemoveAsync(UserLoginKey(login.Provider, login.ProviderKey), ct);
        }

        await cache.RemoveAsync(UserPlanKey(userId), ct);
        await cache.RemoveAsync(BillingHistoryKey(userId), ct);
    }

    public static string UserPlanKey(Guid userId) =>
        $"{UserPlanKeyPrefix}{userId}";
}
