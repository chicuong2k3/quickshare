namespace QuickShare.Data.Entities;

/// <summary>Events a subscription can ask for (issue #39).</summary>
public static class WebhookEvents
{
    public const string DeliveryCreated = "delivery.created";
    public const string DeliveryDownloaded = "delivery.downloaded";
    public const string DeliveryExpired = "delivery.expired";

    public static readonly string[] All = [DeliveryCreated, DeliveryDownloaded, DeliveryExpired];

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Join(',', All);

        var events = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Where(All.Contains)
            .Distinct()
            .ToList();

        return events.Count == 0 ? string.Join(',', All) : string.Join(',', events);
    }

    public static bool Includes(string? subscribed, string eventType) =>
        (subscribed ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(eventType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Where to POST events, and the secret they are signed with (issues #39, #172).
///
/// The secret is stored in the clear because signing requires it back — the same unavoidable
/// trade the SIEM sink makes. It is never returned by any endpoint after creation.
/// </summary>
public record WebhookEndpoint : BaseAuditEntity
{
    public const int UrlMaxLength = 512;
    public const int SecretLength = 64;
    public const int DescriptionMaxLength = 100;
    public const int ErrorMaxLength = 300;

    /// <summary>
    /// Consecutive failures before the endpoint is parked. Retrying a dead URL forever is how a
    /// queue fills with work that will never succeed; parking makes the failure visible instead.
    /// </summary>
    public const int MaxConsecutiveFailures = 20;

    public Guid UserId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Events { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;

    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastDeliveredAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User User { get; init; } = null!;

    public bool IsParked => ConsecutiveFailures >= MaxConsecutiveFailures;
    public bool IsLive => Enabled && RevokedAt is null && !IsParked;
}

public static class WebhookDeliveryStatus
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    /// <summary>Retries exhausted. Kept, and replayable by hand (issue #288).</summary>
    public const string Failed = "failed";
}

/// <summary>
/// One attempt to deliver one event (issues #223 log, #288 replay).
///
/// The row is the log. Keeping the payload means a failed delivery can be replayed exactly as it
/// was first sent — which is the thing an integrator actually needs at 3am, rather than a note
/// saying something failed and is gone.
///
/// The response body is truncated: it is for diagnosis, and an endpoint that returns a megabyte
/// of HTML on error should not be able to fill the database with it.
/// </summary>
public record WebhookDelivery
{
    public const int PayloadMaxLength = 8192;
    public const int ResponseMaxLength = 500;
    /// <summary>Attempts before giving up. Backoff is exponential, so this spans hours, not minutes.</summary>
    public const int MaxAttempts = 6;

    public Guid Id { get; init; }
    public Guid EndpointId { get; init; }
    public Guid UserId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;

    public string Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }

    /// <summary>When the next attempt is due. Null once the row is finished either way.</summary>
    public DateTime? NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public WebhookEndpoint Endpoint { get; init; } = null!;

    /// <summary>
    /// Exponential backoff: 1m, 2m, 4m, 8m, 16m, 32m.
    ///
    /// The point of backing off is that the usual cause of a failed webhook is the receiver being
    /// down or rate-limiting, and hammering it makes both worse.
    /// </summary>
    public static TimeSpan BackoffFor(int attemptCount) =>
        TimeSpan.FromMinutes(Math.Pow(2, Math.Min(attemptCount, MaxAttempts - 1)));
}
