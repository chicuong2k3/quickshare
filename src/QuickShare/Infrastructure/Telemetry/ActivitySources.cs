using System.Diagnostics;

namespace QuickShare.Infrastructure.Telemetry;

public static class ActivitySources
{
    public const string Name = "QuickShare";

    public const string DeliveryExpiryActivity = "delivery-expiry";

    public const string DeliveryCountTag = "delivery.count";

    public static readonly ActivitySource Source = new ActivitySource(Name);
}
