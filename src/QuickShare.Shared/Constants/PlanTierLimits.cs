namespace QuickShare.Shared.Constants;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string Studio = "studio";

    public static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) GetMonthlyLimits(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => (MaxBytesPerMonth: 2_147_483_648L, MaxTransfersPerMonth: null),   // 2 GB
            Pro => (MaxBytesPerMonth: 53_687_091_200L, MaxTransfersPerMonth: null),   // 50 GB
            Studio => (MaxBytesPerMonth: 536_870_912_000L, MaxTransfersPerMonth: null), // 500 GB
            _ => (MaxBytesPerMonth: 2_147_483_648L, MaxTransfersPerMonth: null),
        };
    }

    public static long? GetMaxFileSize(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 104_857_600L,            // 100 MB
            Pro => 2_147_483_648L,           // 2 GB
            Studio => 21_474_836_480L,       // 20 GB
            _ => 104_857_600L,
        };
    }

    public static int GetMaxExpiryDays(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 7,
            Pro => 30,
            Studio => 90,
            _ => 7,
        };
    }

    public static int GetMaxDownloads(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 5,
            Pro => 50,
            Studio => int.MaxValue, // configurable
            _ => 5,
        };
    }

    public static int GetMaxConcurrency(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 2,
            Pro => 4,
            Studio => 6,
            _ => 2,
        };
    }
}
