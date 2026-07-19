namespace QuickShare.Server.Infrastructure;

public sealed class QuickShareOptions
{
    public const string Section = "QuickShare";

    public string[] AllowedCorsOrigins { get; init; } = [];
    public int CleanupIntervalMinutes { get; init; } = 60;
}
