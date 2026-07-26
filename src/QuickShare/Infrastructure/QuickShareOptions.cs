namespace QuickShare.Infrastructure;

public sealed class QuickShareOptions
{
    public const string Section = "QuickShare";

    public int CleanupIntervalMinutes { get; init; } = 60;
    public int MaxP2PConnections { get; init; } = 1000;
}
