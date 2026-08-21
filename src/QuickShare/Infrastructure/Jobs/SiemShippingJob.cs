using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Hangfire;
using QuickShare.Features.Workspaces.Streaming;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Ships each workspace's audit trail to its configured SIEM (issue #191).
///
/// The delivery guarantee is at-least-once, and the mechanism is a cursor rather than an
/// outbox: <c>security_events</c> is already durable and append-only, so the sink stores how
/// far it has read. A failed batch simply does not advance the cursor, and the next run
/// re-sends it. Duplicates on retry are the deliberate trade — a SIEM de-duplicates on
/// <c>externalId</c>, whereas a dropped audit event is unrecoverable and is the one thing the
/// feature exists to prevent.
///
/// A sink that fails ten times running is parked rather than retried forever. Hammering a dead
/// endpoint every five minutes helps nobody, and a parked sink with its last error visible is
/// something an admin can act on; a silently-retrying one is not.
/// </summary>
public sealed class SiemShippingJob(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<SiemShippingJob> logger)
{
    private const string SignatureHeader = "X-QuickShare-Signature";
    private const string TimestampHeader = "X-QuickShare-Timestamp";
    private const string SplunkAuthScheme = "Splunk";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ShipAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sinks = await db.SiemSinks
            .Where(s => s.Enabled && s.ConsecutiveFailures < SiemSink.MaxConsecutiveFailures)
            .ToListAsync(ct);

        foreach (var sink in sinks)
        {
            try
            {
                await ShipSinkAsync(db, sink, ct);
            }
            catch (Exception ex)
            {
                // Never let one broken endpoint stop the others from draining.
                logger.LogWarning(ex, "SIEM shipping failed for workspace {WorkspaceId}", sink.WorkspaceId);
                RecordFailure(sink, ex.Message);
            }
        }

        if (sinks.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task ShipSinkAsync(AppDbContext db, SiemSink sink, CancellationToken ct)
    {
        var events = await ReadBatchAsync(db, sink, ct);
        sink.LastAttemptAt = DateTime.UtcNow;

        if (events.Count == 0)
        {
            // Nothing to send is a success: a quiet workspace should not look like a broken one.
            sink.ConsecutiveFailures = 0;
            sink.LastError = null;
            return;
        }

        var body = SiemEventFormatter.Render(sink.Format, events);
        var response = await PostAsync(sink, body, ct);

        if (!response.IsSuccessStatusCode)
        {
            RecordFailure(sink, $"HTTP {(int)response.StatusCode}");
            return;
        }

        // Advance only on success — this is the whole no-loss guarantee.
        var last = events[^1];
        sink.LastShippedAt = last.Timestamp;
        sink.LastShippedEventId = last.Id;
        sink.ConsecutiveFailures = 0;
        sink.LastError = null;
        sink.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reads forward from the cursor. Ordered by timestamp then id, and the id is the tiebreak
    /// that matters: two events written in the same millisecond would otherwise be ordered
    /// arbitrarily, and a cursor could skip one of them forever.
    /// </summary>
    private static async Task<List<SiemEvent>> ReadBatchAsync(AppDbContext db, SiemSink sink, CancellationToken ct)
    {
        var query = db.SecurityEvents
            .AsNoTracking()
            .Where(e => e.WorkspaceId == sink.WorkspaceId);

        if (sink.LastShippedAt is { } cursor)
        {
            var lastId = sink.LastShippedEventId ?? Guid.Empty;
            query = query.Where(e => e.CreatedAt > cursor || (e.CreatedAt == cursor && e.Id.CompareTo(lastId) > 0));
        }

        var filter = ParseFilter(sink.EventFilter);
        if (filter.Count > 0)
            query = query.Where(e => filter.Contains(e.EventType));

        return await query
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Take(SiemSink.BatchSize)
            .Select(e => new SiemEvent(
                e.Id,
                e.CreatedAt,
                e.EventType.ToString().ToLowerInvariant(),
                e.UserId,
                db.Users.Where(u => u.Id == e.UserId).Select(u => u.Email).FirstOrDefault() ?? string.Empty,
                e.WorkspaceId!.Value,
                e.IpHash,
                e.UserAgentLabel,
                e.MetadataJson))
            .ToListAsync(ct);
    }

    /// <summary>An unparseable name is dropped rather than failing the whole filter.</summary>
    internal static List<SecurityEventType> ParseFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return [];

        var parsed = new List<SecurityEventType>();
        foreach (var name in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SecurityEventType>(name, ignoreCase: true, out var type))
                parsed.Add(type);
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> PostAsync(SiemSink sink, string body, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = RequestTimeout;

        using var request = new HttpRequestMessage(HttpMethod.Post, sink.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(SiemEventFormatter.ContentType(sink.Format));

        if (sink.Format == SiemFormat.SplunkHec)
        {
            // HEC authenticates with its own token scheme; signing on top would be noise.
            request.Headers.Authorization = new AuthenticationHeaderValue(SplunkAuthScheme, sink.Secret);
        }
        else if (!string.IsNullOrEmpty(sink.Secret))
        {
            // Timestamp inside the signed payload so a captured batch cannot be replayed later
            // as if it were fresh.
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp);
            request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(sink.Secret, timestamp, body));
        }

        return await client.SendAsync(request, ct);
    }

    /// <summary>HMAC-SHA256 over <c>{timestamp}.{body}</c>, hex encoded.</summary>
    internal static string Sign(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        return Convert.ToHexStringLower(hmac.ComputeHash(payload));
    }

    private static void RecordFailure(SiemSink sink, string error)
    {
        sink.ConsecutiveFailures++;
        sink.LastError = error.Length > SiemSink.ErrorMaxLength ? error[..SiemSink.ErrorMaxLength] : error;
        sink.LastAttemptAt = DateTime.UtcNow;
        sink.UpdatedAt = DateTime.UtcNow;
    }
}
