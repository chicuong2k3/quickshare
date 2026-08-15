namespace QuickShare.Infrastructure.Constants;

public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string DeliveryNotFound = "delivery_not_found";
    public const string DeliveryExpired = "delivery_expired";
    public const string DeliverySuspended = "delivery_suspended";
    public const string DownloadLimitReached = "download_limit_reached";
    public const string MonthlyQuotaExceeded = "monthly_quota_exceeded";
    public const string SchedulingRequiresPaidPlan = "scheduling_requires_paid_plan";
    public const string DeliverAtMustBeFuture = "deliver_at_must_be_future";
    public const string DeliveryNotActive = "delivery_not_active";
    public const string DeliveryNotScheduled = "delivery_not_scheduled";
}
