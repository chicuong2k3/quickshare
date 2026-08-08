using QuickShare.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace QuickShare.Infrastructure;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string Studio = "studio";
    // Not a purchasable plan — the tier applied to uploads without a signed-in user.
    public const string Anonymous = "anonymous";

    private const string ConfigPrefix = "Pricing:Limits";
    private const string MaxFileSizeKey = "MaxFileSizeBytes";
    private const string MaxMonthlyBytesKey = "MaxMonthlyBytes";
    private const string MaxExpiryHoursKey = "MaxExpiryHours";
    private const string MaxExpiryDaysKey = "MaxExpiryDays";
    private const string MaxDownloadsKey = "MaxDownloads";
    private const string MaxConcurrencyKey = "MaxConcurrency";

    private static readonly FusionCacheEntryOptions PlanLimitsOptions = new()
    {
        Duration = TimeSpan.FromHours(1),
        FailSafeMaxDuration = TimeSpan.FromHours(24),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
    };

    private static CacheService? _cache;
    private static IConfiguration? _config;

    public static void Initialize(IConfiguration config) => _config = config;

    /// <summary>Wire the cache after DI container is built.</summary>
    public static void InitializeCache(CacheService cache) => _cache = cache;

    private static bool CacheEnabled => _cache is not null && _cache.Enabled;

    public static TimeSpan GetMaxExpiry(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetMaxExpiry(plan);

        return _cache!.GetOrSet<TimeSpan>(
            $"plan:maxExpiry:{plan}",
            (_, _) => DirectGetMaxExpiry(plan),
            PlanLimitsOptions
        );
    }

    public static long? GetMaxFileSize(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetMaxFileSize(plan);

        return _cache!.GetOrSet<long?>(
            $"plan:maxFileSize:{plan}",
            (_, _) => DirectGetMaxFileSize(plan),
            PlanLimitsOptions
        );
    }

    public static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) GetMonthlyLimits(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetMonthlyLimits(plan);

        return _cache!.GetOrSet<(long?, int?)>(
            $"plan:monthlyLimits:{plan}",
            (_, _) => DirectGetMonthlyLimits(plan),
            PlanLimitsOptions
        );
    }

    public static int GetMaxDownloads(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetMaxDownloads(plan);

        return _cache!.GetOrSet<int>(
            $"plan:maxDownloads:{plan}",
            (_, _) => DirectGetMaxDownloads(plan),
            PlanLimitsOptions
        );
    }

    public static int GetMaxConcurrency(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetMaxConcurrency(plan);

        return _cache!.GetOrSet<int>(
            $"plan:maxConcurrency:{plan}",
            (_, _) => DirectGetMaxConcurrency(plan),
            PlanLimitsOptions
        );
    }

    /// <summary>Null means unlimited (paid plans). No config read — no cache needed.</summary>
    public static int? GetMaxDailyP2PUploads(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Pro => null,
            Studio => null,
            _ => AdConstants.MaxDailyP2PUploads,
        };
    }

    // ── Direct (uncached) resolvers ──

    private static string Normalize(string plan) => (plan ?? string.Empty).ToLowerInvariant();

    private static TimeSpan DirectGetMaxExpiry(string plan) => plan switch
    {
        Free => TimeSpan.FromHours(GetDouble(Free, MaxExpiryHoursKey, 2.0)),
        Pro => TimeSpan.FromDays(GetInt(Pro, MaxExpiryDaysKey, 30)),
        Studio => TimeSpan.FromDays(GetInt(Studio, MaxExpiryDaysKey, 90)),
        _ => TimeSpan.FromHours(GetDouble(Free, MaxExpiryHoursKey, 2.0)),
    };

    private static long? DirectGetMaxFileSize(string plan) => plan switch
    {
        Free => GetLong(Free, MaxFileSizeKey, 5_368_709_120L),
        Pro => GetLong(Pro, MaxFileSizeKey, 21_474_836_480L),
        Studio => GetLong(Studio, MaxFileSizeKey, 107_374_182_400L),
        // Anonymous stays small — the sign-up nudge for uploads without an account.
        Anonymous => GetLong(Anonymous, MaxFileSizeKey, 104_857_600L),
        _ => GetLong(Free, MaxFileSizeKey, 5_368_709_120L),
    };

    private static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) DirectGetMonthlyLimits(string plan) => plan switch
    {
        Free => (GetLong(Free, MaxMonthlyBytesKey, 16_106_127_360L), null),
        Pro => (GetLong(Pro, MaxMonthlyBytesKey, 214_748_364_800L), null),
        Studio => (GetLong(Studio, MaxMonthlyBytesKey, 2_199_023_255_552L), null),
        _ => (GetLong(Free, MaxMonthlyBytesKey, 16_106_127_360L), null),
    };

    private static int DirectGetMaxDownloads(string plan) => plan switch
    {
        Free => GetInt(Free, MaxDownloadsKey, 10),
        Pro => GetInt(Pro, MaxDownloadsKey, 100),
        Studio => GetInt(Studio, MaxDownloadsKey, int.MaxValue),
        _ => GetInt(Free, MaxDownloadsKey, 10),
    };

    private static int DirectGetMaxConcurrency(string plan) => plan switch
    {
        Free => GetInt(Free, MaxConcurrencyKey, 2),
        Pro => GetInt(Pro, MaxConcurrencyKey, 5),
        Studio => GetInt(Studio, MaxConcurrencyKey, 10),
        _ => GetInt(Free, MaxConcurrencyKey, 2),
    };

    // ── Config helpers ──

    private static double GetDouble(string tier, string key, double fallback)
    {
        var val = _config?.GetValue<double?>($"{ConfigPrefix}:{tier}:{key}");
        return val ?? fallback;
    }

    private static int GetInt(string tier, string key, int fallback)
    {
        var val = _config?.GetValue<int?>($"{ConfigPrefix}:{tier}:{key}");
        return val ?? fallback;
    }

    private static long? GetLong(string tier, string key, long? fallback)
    {
        var val = _config?.GetValue<long?>($"{ConfigPrefix}:{tier}:{key}");
        return val ?? fallback;
    }
}
