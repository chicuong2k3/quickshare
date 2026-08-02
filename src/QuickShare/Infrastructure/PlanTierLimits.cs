using Microsoft.Extensions.Configuration;

namespace QuickShare.Infrastructure;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string Studio = "studio";
    public const string Anonymous = "anonymous";

    private const string ConfigPrefix = "Pricing:Limits";
    private const string MaxFileSizeKey = "MaxFileSizeBytes";
    private const string MaxMonthlyBytesKey = "MaxMonthlyBytes";
    private const string MaxExpiryHoursKey = "MaxExpiryHours";
    private const string MaxExpiryDaysKey = "MaxExpiryDays";
    private const string MaxDownloadsKey = "MaxDownloads";
    private const string MaxConcurrencyKey = "MaxConcurrency";

    private static IConfiguration? _config;

    public static void Initialize(IConfiguration config) => _config = config;

    /// <summary>Returns the max expiry as a TimeSpan. Free tier uses hours; paid tiers use days.</summary>
    public static TimeSpan GetMaxExpiry(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => TimeSpan.FromHours(GetDouble(Free, MaxExpiryHoursKey, 2.0)),
            Pro => TimeSpan.FromDays(GetInt(Pro, MaxExpiryDaysKey, 30)),
            Studio => TimeSpan.FromDays(GetInt(Studio, MaxExpiryDaysKey, 90)),
            _ => TimeSpan.FromHours(GetDouble(Free, MaxExpiryHoursKey, 2.0)),
        };
    }

    public static long? GetMaxFileSize(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => GetLong(Free, MaxFileSizeKey, 5_368_709_120L),
            Pro => GetLong(Pro, MaxFileSizeKey, 21_474_836_480L),
            Studio => GetLong(Studio, MaxFileSizeKey, 107_374_182_400L),
            _ => GetLong(Free, MaxFileSizeKey, 5_368_709_120L),
        };
    }

    public static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) GetMonthlyLimits(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => (GetLong(Free, MaxMonthlyBytesKey, 16_106_127_360L), null),
            Pro => (GetLong(Pro, MaxMonthlyBytesKey, 214_748_364_800L), null),
            Studio => (GetLong(Studio, MaxMonthlyBytesKey, 2_199_023_255_552L), null),
            _ => (GetLong(Free, MaxMonthlyBytesKey, 16_106_127_360L), null),
        };
    }

    public static int GetMaxDownloads(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => GetInt(Free, MaxDownloadsKey, 10),
            Pro => GetInt(Pro, MaxDownloadsKey, 100),
            Studio => GetInt(Studio, MaxDownloadsKey, int.MaxValue),
            _ => GetInt(Free, MaxDownloadsKey, 10),
        };
    }

    public static int GetMaxConcurrency(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => GetInt(Free, MaxConcurrencyKey, 2),
            Pro => GetInt(Pro, MaxConcurrencyKey, 5),
            Studio => GetInt(Studio, MaxConcurrencyKey, 10),
            _ => GetInt(Free, MaxConcurrencyKey, 2),
        };
    }

    /// <summary>Null means unlimited (paid plans).</summary>
    public static int? GetMaxDailyP2PUploads(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Pro => null,
            Studio => null,
            _ => AdConstants.MaxDailyP2PUploads,
        };
    }

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
