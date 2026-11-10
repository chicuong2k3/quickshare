namespace QuickShare.Data.Entities;

/// <summary>
/// Tracked affiliate partner profile (issue #298).
/// Distinct from the storage-bonus referral program (#15/#31): affiliates earn a percentage
/// commission on the first paid subscription of users they refer via <c>?aff=CODE</c>.
/// </summary>
public record AffiliateProfile : BaseAuditEntity
{
    public const int CodeMaxLength = 32;
    public const int PayoutInfoMaxLength = 300;
    public const int TaxIdMaxLength = 64;
    public const int DefaultCommissionPercent = 20;

    public const string StatusActive = "active";
    public const string StatusSuspended = "suspended";

    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int CommissionPercent { get; set; } = DefaultCommissionPercent;
    public string PayoutMethod { get; set; } = "bank_transfer";
    public string PayoutDetails { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public bool TermsAccepted { get; set; }
    public long ClicksCount { get; set; }
    public long SignupsCount { get; set; }
    public long PaidConversionsCount { get; set; }
    public decimal TotalEarnedVnd { get; set; }
    public decimal PendingPayoutVnd { get; set; }
    public string Status { get; set; } = StatusActive;

    public User User { get; init; } = null!;
}

/// <summary>
/// Commission ledger entry for an affiliate conversion (issue #298).
/// </summary>
public record AffiliateConversion : BaseAuditEntity
{
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusPaid = "paid";

    public Guid AffiliateProfileId { get; set; }
    public Guid ReferredUserId { get; set; }
    public Guid? PaymentIntentId { get; set; }
    public string Plan { get; set; } = string.Empty;
    public decimal SaleAmountVnd { get; set; }
    public decimal CommissionAmountVnd { get; set; }
    public string Status { get; set; } = StatusApproved;

    public AffiliateProfile AffiliateProfile { get; init; } = null!;
}
