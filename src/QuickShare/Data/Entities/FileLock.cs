namespace QuickShare.Data.Entities;

/// <summary>
/// Checkout lock on a delivery (issue #308): while an active lock exists, only the
/// holder may replace the file — everyone else gets a 423. A lock is active when
/// <see cref="ReleasedAt"/> is null and <see cref="ExpiresAt"/> is in the future.
/// The sender (owner) may force-break any lock, which notifies the holder.
/// </summary>
public record FileLock : BaseAuditEntity
{
    public const int ReasonMaxLength = 200;

    public Guid DeliveryId { get; init; }
    public Guid LockedById { get; init; }
    public string? Reason { get; init; }
    public DateTime ExpiresAt { get; init; }
    /// <summary>Set when the lock is released or force-broken; null = still active.</summary>
    public DateTime? ReleasedAt { get; set; }
}