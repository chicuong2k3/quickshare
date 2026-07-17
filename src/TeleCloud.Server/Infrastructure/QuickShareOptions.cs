namespace TeleCloud.Server.Infrastructure;

public sealed class QuickShareOptions
{
    public const string Section = "QuickShare";

    /// <summary>Public base URL of the web app, used to build human-facing share links ({PublicBaseUrl}/t/{id}).</summary>
    public string PublicBaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>Origins allowed to call the API from the browser (CORS). Empty → no origins configured.</summary>
    public string[] AllowedCorsOrigins { get; init; } = [];

    /// <summary>Interval in minutes between temp-share cleanup ticks.</summary>
    public int CleanupIntervalMinutes { get; init; } = 15;
}
