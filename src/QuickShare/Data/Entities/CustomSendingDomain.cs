namespace QuickShare.Data.Entities;

/// <summary>
/// Custom sending domain for outbound recipient emails (issue #150, extends #124/#108).
/// Allows Pro/Studio accounts to verify SPF/DKIM/DMARC DNS records and send delivery emails
/// from <c>share@yourname.com</c>, with automatic fallback to the platform sender on failure.
/// </summary>
[DynamicSort]
public record CustomSendingDomain : BaseAuditEntity
{
    public const int DomainMaxLength = 253;
    public const int FromLocalPartMaxLength = 64;
    public const int TokenLength = 32;
    public const int RecordValueMaxLength = 512;

    public const string StatusPending = "pending";
    public const string StatusVerified = "verified";
    public const string StatusFailed = "failed";

    public Guid UserId { get; init; }
    public Guid? WorkspaceId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string FromLocalPart { get; set; } = "share";
    public string Status { get; set; } = StatusPending;
    public string VerificationToken { get; set; } = string.Empty;
    public string SpfRecord { get; set; } = string.Empty;
    public string DkimHost { get; set; } = string.Empty;
    public string DkimValue { get; set; } = string.Empty;
    public string DmarcRecord { get; set; } = string.Empty;
    public bool SpfVerified { get; set; }
    public bool DkimVerified { get; set; }
    public bool DmarcVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public string? LastError { get; set; }

    public string FullFromAddress => $"{FromLocalPart}@{Domain}";
    public bool IsVerified => Status == StatusVerified && VerifiedAt is not null;
}
