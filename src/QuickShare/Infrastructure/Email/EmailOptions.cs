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

    /// <summary>
    /// Domain the provider delivers forwarded mail on (issue #104), e.g. <c>inbound.example.com</c>.
    /// Empty means this deployment cannot receive mail, and no ingest address is ever minted.
    /// </summary>
    public string InboundDomain { get; init; } = string.Empty;

    /// <summary>Shared secret the inbound webhook must present. Empty refuses every call.</summary>
    public string InboundSecret { get; init; } = string.Empty;
    public int DigestIntervalMinutes { get; init; } = 15;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(From);
}
