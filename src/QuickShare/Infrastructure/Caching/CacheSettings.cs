namespace QuickShare.Infrastructure.Caching;

public sealed class CacheSettings
{
    public const string Section = "FusionCache";

    /// <summary>Master kill switch. When false, all cache operations are no-ops (always calls factory).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Default cache entry duration in minutes.</summary>
    public int DefaultDurationMinutes { get; set; } = 5;

    /// <summary>How long fail-safe keeps serving stale data after expiry.</summary>
    public int FailSafeMaxDurationHours { get; set; } = 2;

    /// <summary>How long stale entries are throttled during fail-safe.</summary>
    public int FailSafeThrottleDurationSeconds { get; set; } = 30;

    /// <summary>TTL for PlanTierLimits entries (config rarely changes).</summary>
    public int PlanTierLimitsDurationHours { get; set; } = 1;
}
