namespace QuickShare.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string FromName { get; init; } = "QuickShare";
    public string BaseUrl { get; init; } = string.Empty;
    public int DigestIntervalMinutes { get; init; } = 15;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(From);
}
