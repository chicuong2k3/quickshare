namespace QuickShare.Data.Entities;

/// <summary>
/// An email address suppressed from outbound mail due to a hard bounce or spam complaint (issue #108).
///
/// Stored normalized (lowercase, trimmed). <see cref="QuickShare.Infrastructure.Email.EmailSender"/>
/// consults this table before opening an SMTP connection so a dead or complaining recipient is never
/// contacted again until an operator explicitly releases the address on <c>/admin/email-suppressions</c>.
/// </summary>
public record EmailSuppression : BaseAuditEntity
{
    public const int EmailMaxLength = 256;
    public const int ReasonMaxLength = 64;
    public const int SourceMaxLength = 64;

    public const string ReasonHardBounce = "hard_bounce";
    public const string ReasonSoftBounce = "soft_bounce";
    public const string ReasonSpamComplaint = "spam_complaint";
    public const string ReasonUnsubscribed = "unsubscribed";

    public const string SourceBrevoWebhook = "brevo_webhook";
    public const string SourceUnsubscribeLink = "unsubscribe_link";

    public string Email { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int BounceCount { get; set; } = 1;

    public static string Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
}
