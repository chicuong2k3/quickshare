namespace QuickShare.Data.Entities;

/// <summary>
/// External penetration test record and posture tracker (issue #188).
/// Tracks annual third-party assessments, remediation counts, and retest verification.
/// </summary>
public record PenetrationTestRecord : BaseAuditEntity
{
    public const int VendorMaxLength = 120;
    public const int ScopeMaxLength = 250;
    public const int MethodologyMaxLength = 120;
    public const int SummaryMaxLength = 1000;
    public const int ReportRefMaxLength = 120;
    public const int StatusCompleted = 1;

    public const string StatusScheduled = "scheduled";
    public const string StatusInRemediation = "in_remediation";
    public const string StatusVerifiedClean = "verified_clean";

    public int AssessmentYear { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Methodology { get; set; } = "OWASP Top 10 + API Security Top 10";
    public DateTime ConductedAt { get; set; }
    public DateTime? RetestedAt { get; set; }
    public DateTime NextScheduledAt { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public int RemediatedCount { get; set; }
    public string Status { get; set; } = StatusVerifiedClean;
    public string PublicSummary { get; set; } = string.Empty;
    public string? NdaReportReference { get; set; }
}
