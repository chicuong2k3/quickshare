namespace QuickShare.Data.Entities;

/// <summary>
/// Anonymous abuse/DMCA report against a delivery (issue #53). Reporter
/// identity is deliberately minimal — only a hashed IP is stored (privacy #26).
/// </summary>
public record AbuseReport
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public string Category { get; init; } = string.Empty;
    public string? Note { get; init; }
    public string ReporterIpHash { get; init; } = string.Empty;
    public string Status { get; set; } = AbuseReportStatus.Open;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
}

public static class AbuseReportStatus
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";
}

public static class AbuseReportCategory
{
    public const string Abuse = "abuse";
    public const string Copyright = "copyright";
    public const string Malware = "malware";
    public const string Other = "other";
}
