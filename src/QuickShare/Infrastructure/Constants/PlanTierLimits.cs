using QuickShare.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace QuickShare.Infrastructure.Constants;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string Studio = "studio";
    // Not a purchasable plan — the tier applied to uploads without a signed-in user.
    public const string Anonymous = "anonymous";

    // Monthly upload quotas (bytes). Advertised on /pricing; enforced at start-r2
    // (declared size) and commit-r2 (actual size). Anonymous has no monthly cap.
    public const long FreeMonthlyQuotaBytes = 15L * 1024 * 1024 * 1024;      // 15 GB
    public const long ProMonthlyQuotaBytes = 200L * 1024 * 1024 * 1024;     // 200 GB
    public const long StudioMonthlyQuotaBytes = 2L * 1024 * 1024 * 1024 * 1024; // 2 TB

    private const string ConfigPrefix = "Pricing:Limits";
    private const string MaxFileSizeKey = "MaxFileSizeBytes";
    private const string MaxExpiryHoursKey = "MaxExpiryHours";
    private const string MaxExpiryDaysKey = "MaxExpiryDays";
    private const string MaxDownloadsKey = "MaxDownloads";
    private const string RequestMaxTotalBytesKey = "RequestMaxTotalBytes";
    private const string RequestMaxFilesKey = "RequestMaxFiles";
    private const string RequestMaxExpiryDaysKey = "RequestMaxExpiryDays";

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

    /// <summary>Monthly upload quota in bytes. Null means no monthly cap (anonymous).</summary>
    public static long? GetMonthlyQuota(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => FreeMonthlyQuotaBytes,
            Pro => ProMonthlyQuotaBytes,
            Studio => StudioMonthlyQuotaBytes,
            _ => null,
        };
    }

    // ── File request caps (issue #16) ── submissions never count against owner monthly quota

    public static long? GetRequestMaxTotalBytes(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetRequestMaxTotalBytes(plan);

        return _cache!.GetOrSet<long?>(
            $"plan:requestMaxTotalBytes:{plan}",
            (_, _) => DirectGetRequestMaxTotalBytes(plan),
            PlanLimitsOptions
        );
    }

    public static int GetRequestMaxFiles(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetRequestMaxFiles(plan);

        return _cache!.GetOrSet<int>(
            $"plan:requestMaxFiles:{plan}",
            (_, _) => DirectGetRequestMaxFiles(plan),
            PlanLimitsOptions
        );
    }

    public static TimeSpan GetRequestMaxExpiry(string plan)
    {
        plan = Normalize(plan);
        if (!CacheEnabled) return DirectGetRequestMaxExpiry(plan);

        return _cache!.GetOrSet<TimeSpan>(
            $"plan:requestMaxExpiry:{plan}",
            (_, _) => DirectGetRequestMaxExpiry(plan),
            PlanLimitsOptions
        );
    }

    private static long? DirectGetRequestMaxTotalBytes(string plan) => plan switch
    {
        Free => GetLong(Free, RequestMaxTotalBytesKey, 2L * 1024 * 1024 * 1024),
        Pro => GetLong(Pro, RequestMaxTotalBytesKey, 50L * 1024 * 1024 * 1024),
        Studio => GetLong(Studio, RequestMaxTotalBytesKey, 500L * 1024 * 1024 * 1024),
        _ => GetLong(Free, RequestMaxTotalBytesKey, 2L * 1024 * 1024 * 1024),
    };

    private static int DirectGetRequestMaxFiles(string plan) => plan switch
    {
        Free => GetInt(Free, RequestMaxFilesKey, 20),
        Pro => GetInt(Pro, RequestMaxFilesKey, 500),
        Studio => GetInt(Studio, RequestMaxFilesKey, 2000),
        _ => GetInt(Free, RequestMaxFilesKey, 20),
    };

    private static TimeSpan DirectGetRequestMaxExpiry(string plan) => plan switch
    {
        Free => TimeSpan.FromDays(GetInt(Free, RequestMaxExpiryDaysKey, 7)),
        Pro => TimeSpan.FromDays(GetInt(Pro, RequestMaxExpiryDaysKey, 30)),
        Studio => TimeSpan.FromDays(GetInt(Studio, RequestMaxExpiryDaysKey, 90)),
        _ => TimeSpan.FromDays(GetInt(Free, RequestMaxExpiryDaysKey, 7)),
    };

    // ── Direct (uncached) resolvers ──

    private static string Normalize(string plan) => (plan ?? string.Empty).ToLowerInvariant();

    private static TimeSpan DirectGetMaxExpiry(string plan) => plan switch
    {
        Free => TimeSpan.FromHours(GetDouble(Free, MaxExpiryHoursKey, 168.0)),
        Pro => TimeSpan.FromDays(GetInt(Pro, MaxExpiryDaysKey, 30)),
        Studio => TimeSpan.FromDays(GetInt(Studio, MaxExpiryDaysKey, 90)),
        _ => TimeSpan.FromHours(GetDouble(Free, MaxExpiryHoursKey, 168.0)),
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

    private static int DirectGetMaxDownloads(string plan) => plan switch
    {
        Free => GetInt(Free, MaxDownloadsKey, 10),
        Pro => GetInt(Pro, MaxDownloadsKey, 100),
        Studio => GetInt(Studio, MaxDownloadsKey, int.MaxValue),
        _ => GetInt(Free, MaxDownloadsKey, 10),
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
