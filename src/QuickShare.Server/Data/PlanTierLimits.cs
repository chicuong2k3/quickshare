namespace QuickShare.Server.Data;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Starter = "starter";
    public const string Ultimate = "ultimate";

    public static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) GetMonthlyLimits(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => (MaxBytesPerMonth: 3_221_225_472L, MaxTransfersPerMonth: 10),    // 3 GB
            Starter => (MaxBytesPerMonth: 322_122_547_200L, MaxTransfersPerMonth: 10), // 300 GB
            Ultimate => (MaxBytesPerMonth: null, MaxTransfersPerMonth: null),          // unlimited
            _ => (MaxBytesPerMonth: 3_221_225_472L, MaxTransfersPerMonth: 10),
        };
    }

    public static long? GetMaxFileSize(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 5L * 1024 * 1024 * 1024,       // 5 GB
            Starter => 50L * 1024 * 1024 * 1024,    // 50 GB
            Ultimate => null,                        // unlimited
            _ => 5L * 1024 * 1024 * 1024,
        };
    }

    public static int GetMaxExpiryDays(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 3,
            Starter => 30,
            Ultimate => 30,
            _ => 3,
        };
    }

    public static int GetMaxDownloads(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 5,
            Starter => 50,
            Ultimate => int.MaxValue, // effectively unlimited
            _ => 5,
        };
    }

    public static int GetMaxConcurrency(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => 2,
            Starter => 4,
            Ultimate => 6,
            _ => 2,
        };
    }
}
