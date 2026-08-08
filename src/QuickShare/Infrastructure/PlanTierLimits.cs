namespace QuickShare.Infrastructure;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string Studio = "studio";
    // Not a purchasable plan — the tier applied to uploads without a signed-in user.
    // Falls through to the default (most restrictive) branch of every limit switch.
    public const string Anonymous = "anonymous";

    public static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) GetMonthlyLimits(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => (MaxBytesPerMonth: 10_737_418_240L, MaxTransfersPerMonth: null),  // 10 GB
            Pro => (MaxBytesPerMonth: 53_687_091_200L, MaxTransfersPerMonth: null),   // 50 GB
            Studio => (MaxBytesPerMonth: 536_870_912_000L, MaxTransfersPerMonth: null), // 500 GB
            _ => (MaxBytesPerMonth: 2_147_483_648L, MaxTransfersPerMonth: null),
        };
    }

    public static long? GetMaxFileSize(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 2_147_483_648L,          // 2 GB — WeTransfer free parity
            Pro => 5_368_709_120L,           // 5 GB — matches the client-side MAX_FILE_BYTES cap
            Studio => 21_474_836_480L,       // 20 GB (client currently caps at 5 GB)
            _ => 104_857_600L,               // 100 MB — anonymous
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
