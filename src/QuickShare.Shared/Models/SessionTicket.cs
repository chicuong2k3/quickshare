namespace QuickShare.Shared.Models;

public record SessionTicket
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public byte[] Value { get; init; } = [];
    public DateTimeOffset ExpiresAt { get; init; }

    // Schema: "cookie-auth"
    public string SchemeName { get; init; } = string.Empty;
}
