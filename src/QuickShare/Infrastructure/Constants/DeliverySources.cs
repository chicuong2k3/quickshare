namespace QuickShare.Infrastructure.Constants;

/// <summary>
/// Where an upload came from. Client-declared, so it is a metric — never a permission.
/// </summary>
public static class DeliverySources
{
    public const string Web = "web";
    public const string Paste = "paste";
    public const string Url = "url";
    /// <summary>A protocol mount — WebDAV or the S3 gateway (issues #195, #265).</summary>
    public const string Mount = "mount";

    private static readonly string[] Known = [Web, Paste, Url, Mount];

    public static string Normalize(string? source)
        => Known.Contains(source) ? source! : Web;
}
