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
    /// <summary>Forwarded to the account's private ingest address (issue #104).</summary>
    public const string Email = "email";

    private static readonly string[] Known = [Web, Paste, Url, Mount, Email, Cloud, OfflineQueue, Sftp, GitHubRelease, Cli, TelegramBot, DiscordBot];

    public const string Cloud = "cloud";
    public const string OfflineQueue = "offline_queue";
    public const string Sftp = "sftp";
    public const string GitHubRelease = "github_release";
    public const string Cli = "cli";
    public const string TelegramBot = "telegram_bot";
    public const string DiscordBot = "discord_bot";

    public static string Normalize(string? source)
        => Known.Contains(source) ? source! : Web;
}
