namespace QuickShare.Data.Entities;

[DynamicSort]
public record UserUsage
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateTime PeriodStart { get; init; }
    public long TotalBytesUploaded { get; set; }
    /// <summary>
    /// Bytes uploaded past the plan quota this period (issue #141). Studio keeps sharing and
    /// pays per GB; every other tier hard-blocks, so this stays zero for them.
    /// </summary>
    public long OverageBytes { get; set; }
    /// <summary>
    /// Highest quota-usage alert already sent this period, as a percentage (80/100/120).
    /// Stored so a threshold fires once instead of on every upload past the line.
    /// </summary>
    public int AlertedThresholdPercent { get; set; }
    public long DailyBandwidthBonus { get; set; }
    public int TransferCount { get; set; }

    public User User { get; init; } = null!;
}
