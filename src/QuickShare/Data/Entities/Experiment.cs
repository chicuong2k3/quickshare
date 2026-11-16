namespace QuickShare.Data.Entities;

/// <summary>
/// Server-side A/B experiment definition (issue #92).
/// Buckets visitors deterministically by hashing <c>(qs_visitor, Key)</c>, so no extra PII or
/// third-party script is needed and the same visitor sees a consistent variant across requests.
/// </summary>
public record Experiment : BaseAuditEntity
{
    public const int KeyMaxLength = 64;
    public const int NameMaxLength = 120;
    public const int CopyMaxLength = 240;

    public const string StatusRunning = "running";
    public const string StatusPromoted = "promoted";
    public const string StatusPaused = "paused";

    public const string VariantA = "A";
    public const string VariantB = "B";

    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surface { get; set; } = "landing";
    public string VariantACopy { get; set; } = string.Empty;
    public string VariantBCopy { get; set; } = string.Empty;
    public int TrafficSplitPercent { get; set; } = 50;
    public string Status { get; set; } = StatusRunning;
    public string? WinnerVariant { get; set; }
}
