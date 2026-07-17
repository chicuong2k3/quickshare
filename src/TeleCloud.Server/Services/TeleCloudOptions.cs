namespace TeleCloud.Server.Services;

public sealed class TeleCloudOptions
{
    public const string Section = "TeleCloud";

    public string ApiId { get; init; } = string.Empty;
    public string ApiHash { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string SessionPathname { get; init; } = "WTelegram.session";
    public string StorageChannel { get; init; } = string.Empty;
}
