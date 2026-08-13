namespace QuickShare.Infrastructure.Constants;

/// <summary>
/// Where an upload came from. Client-declared, so it is a metric — never a permission.
/// </summary>
public static class DeliverySources
{
    public const string Web = "web";
    public const string Paste = "paste";

    private static readonly string[] Known = [Web, Paste];

    public static string Normalize(string? source)
        => Known.Contains(source) ? source! : Web;
}
