namespace TeleCloud.Server.Data;

public static class PlanTierLimits
{
    public const string Free = "free";
    public const string Starter = "starter";
    public const string Ultimate = "ultimate";

    public static (long? MaxBytesPerMonth, int? MaxTransfersPerMonth) GetLimits(string plan)
    {
        return (plan ?? "").ToLowerInvariant() switch
        {
            Free => (MaxBytesPerMonth: 3_221_225_472L, MaxTransfersPerMonth: 10),    // 3 GB
            Starter => (MaxBytesPerMonth: 322_122_547_200L, MaxTransfersPerMonth: 10), // 300 GB
            Ultimate => (MaxBytesPerMonth: null, MaxTransfersPerMonth: null),          // unlimited
            _ => (MaxBytesPerMonth: 3_221_225_472L, MaxTransfersPerMonth: 10),         // safe default => free
        };
    }
}
