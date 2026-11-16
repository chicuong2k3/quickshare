namespace QuickShare.Features.Deliveries.Lock;

/// <summary>TTL bounds for delivery file locks (issue #308).</summary>
public static class FileLockLimits
{
    public const int DefaultDurationMinutes = 24 * 60; // 1440
    public const int MaxDurationMinutes = 7 * 24 * 60; // 10080
}