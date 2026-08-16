namespace QuickShare.Infrastructure.Constants;

public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string BadRequest = "bad_request";
    public const string DeliveryNotFound = "delivery_not_found";
    public const string DeliveryExpired = "delivery_expired";
    public const string DeliverySuspended = "delivery_suspended";
    public const string DeliveryRevoked = "delivery_revoked";
    public const string DeliveryNoExpiry = "delivery_no_expiry";
    public const string FileNameInvalid = "file_name_invalid";
    public const string DownloadLimitReached = "download_limit_reached";
    public const string DeliveryDailyLimitReached = "delivery_daily_limit_reached";
    public const string DailyLimitInvalid = "daily_limit_invalid";
    public const string RequiresPaidPlan = "requires_paid_plan";
    public const string AliasInvalid = "alias_invalid";
    public const string AliasTaken = "alias_taken";
    public const string MonthlyQuotaExceeded = "monthly_quota_exceeded";
    public const string SchedulingRequiresPaidPlan = "scheduling_requires_paid_plan";
    public const string DeliverAtMustBeFuture = "deliver_at_must_be_future";
    public const string DeliveryNotActive = "delivery_not_active";
    public const string DeliveryNotScheduled = "delivery_not_scheduled";
    public const string MessageTooLong = "message_too_long";
    public const string DeliveryAlreadyDownloaded = "delivery_already_downloaded";
    public const string DeliveryGated = "delivery_gated";
    public const string TrialRequiresFreePlan = "trial_requires_free_plan";
    public const string TrialAlreadyUsed = "trial_already_used";
    public const string ExpiryInvalid = "expiry_invalid";
    public const string ExpiryTooLong = "expiry_too_long";
    public const string PasswordTooLong = "password_too_long";
    public const string Sha256Invalid = "sha256_invalid";
    public const string ExtendNotAllowed = "extend_not_allowed";
    public const string ExtendTooEarly = "extend_too_early";
    public const string ExtendTooLate = "extend_too_late";
    public const string TagTooLong = "tag_too_long";
    public const string AccountDeleteConfirmMismatch = "account_delete_confirm_mismatch";
}
