namespace QuickShare.Data.Entities;

/// <summary>
/// How many calls a key made on a day (issue #273).
///
/// A daily bucket rather than a row per request: the question a developer asks is “is my
/// integration still running, and how hard”, and that is a shape a graph answers. Per-request
/// rows would be a log — a different feature, with retention and privacy questions attached.
/// </summary>
public record ApiKeyUsage
{
    public Guid Id { get; init; }
    public Guid ApiKeyId { get; init; }

    /// <summary>UTC date. One row per key per day.</summary>
    public DateOnly Day { get; init; }

    public int Requests { get; set; }

    public ApiKey ApiKey { get; init; } = null!;
}
