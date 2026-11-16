namespace QuickShare.Data.Entities;

/// <summary>
/// Eligibility verification for student and nonprofit discounts (issue #203).
///
/// One active application per account (<c>UserId</c> unique index). University email domains
/// (<c>.edu</c>, <c>.edu.vn</c>, <c>.ac.vn</c>, <c>.ac.uk</c>) are auto-verified on submission
/// and mint a personal, non-shareable promo code tied to the applicant's email. Manual student
/// ID or nonprofit registration submissions enter the admin review queue and mint their code on
/// approval. Annual re-verification is tracked via <see cref="ExpiresAt"/>.
/// </summary>
public record DiscountApplication : BaseAuditEntity
{
    public const int KindMaxLength = 24;
    public const int StatusMaxLength = 24;
    public const int OrganizationMaxLength = 120;
    public const int VerificationEmailMaxLength = 254;
    public const int ProofReferenceMaxLength = 200;

    public const string KindStudent = "student";
    public const string KindNonprofit = "nonprofit";

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    public const int StudentPercentOff = 50;
    public const int NonprofitPercentOff = 50;

    public Guid UserId { get; set; }
    public string Kind { get; set; } = KindStudent;
    public string OrganizationName { get; set; } = string.Empty;
    public string VerificationEmail { get; set; } = string.Empty;
    public string? ProofReference { get; set; }
    public string Status { get; set; } = StatusPending;
    public string? IssuedPromoCode { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public User User { get; init; } = null!;

    public static bool IsAutoVerifiedAcademicEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim().ToLowerInvariant();
        var atIndex = trimmed.LastIndexOf('@');
        if (atIndex < 0 || atIndex == trimmed.Length - 1)
        {
            return false;
        }

        var domain = trimmed[(atIndex + 1)..];
        return domain.EndsWith(".edu", StringComparison.Ordinal)
            || domain.EndsWith(".edu.vn", StringComparison.Ordinal)
            || domain.EndsWith(".ac.vn", StringComparison.Ordinal)
            || domain.EndsWith(".ac.uk", StringComparison.Ordinal);
    }
}
