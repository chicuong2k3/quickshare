namespace QuickShare.Data.Entities;

/// <summary>
/// Per-delivery OTP brute-force lockout state (issue #148). One row per
/// delivery; failed attempts escalate the lock (15min → 1h → 24h).
/// </summary>
public record OtpLockout
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public int FailedAttempts { get; set; }
    public int LockStage { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class OtpLockoutPolicy
{
    /// <summary>Failed attempts before the first lockout kicks in.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Escalating lock durations per stage: 15min, 1h, 24h.</summary>
    public static readonly TimeSpan[] LockDurations =
    [
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(24)
    ];

    public static TimeSpan LockDurationForStage(int stage) =>
        LockDurations[Math.Clamp(stage, 0, LockDurations.Length - 1)];
}
