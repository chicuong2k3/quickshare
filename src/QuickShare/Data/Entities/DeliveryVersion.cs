namespace QuickShare.Data.Entities;

/// <summary>
/// One previous version of a delivery's file (issue #110).
///
/// When a sender replaces a file (#72) on a Pro or Studio account, the current bytes are
/// snapshotted here before the delivery record is updated. The delivery always serves the
/// latest version; older ones are downloadable by the sender from /account/deliveries and
/// by any recipient who has the share link and selects a version.
///
/// Free plan: replace still overwrites (no snapshot) — storage cost is the gate.
/// Pro plan: up to <see cref="MaxVersionsPro"/> snapshots kept per delivery.
/// Studio: unlimited.
///
/// Snapshots are deleted when the delivery is permanently purged or when its expiry job
/// runs; they are NOT subject to the delivery's own download count or daily cap.
/// </summary>
public record DeliveryVersion : BaseAuditEntity
{
    public const int MaxVersionsPro = 10;

    public Guid DeliveryId { get; set; }

    /// <summary>1-based. The first time a file is replaced the current bytes become version 1.</summary>
    public int VersionNumber { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? Sha256 { get; set; }

    public Delivery Delivery { get; init; } = null!;
}
