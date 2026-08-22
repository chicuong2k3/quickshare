using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QuickShare.Features.Developer.ApiKeys;

namespace QuickShare.Features.Developer.Webhooks;

/// <summary>
/// Queues webhook events and sends them (issues #39, #172, #223, #288).
///
/// Queued, never sent inline. The Slack/Teams notifier (#180) posts inline and documents the
/// trade — a share must not fail because a chat workspace is down — but that only holds because
/// it is best-effort and forgettable. A webhook is an integration contract: it has to survive the
/// receiver being down, which means a row, a retry schedule and a job, not a fire-and-forget call
/// on the request thread.
/// </summary>
public sealed class WebhookDispatcher(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookDispatcher> logger)
{
    /// <summary>Long enough for a slow receiver, short enough that the job keeps moving.</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public const string SignatureHeader = "X-QuickShare-Signature";
    public const string TimestampHeader = "X-QuickShare-Timestamp";
    public const string EventHeader = "X-QuickShare-Event";
    public const string DeliveryHeader = "X-QuickShare-Delivery";

    /// <summary>
    /// Records the event for every live endpoint of this account that asked for it.
    ///
    /// Best-effort and never throws: an upload must not fail because a webhook row could not be
    /// written. The caller is on the request path.
    /// </summary>
    public async Task EnqueueAsync(Guid? userId, string eventType, object payload, CancellationToken ct)
    {
        if (userId is null)
            return;

        try
        {
            var endpoints = await db.WebhookEndpoints
                .Where(e => e.UserId == userId.Value && e.Enabled && e.RevokedAt == null)
                .ToListAsync(ct);

            if (endpoints.Count == 0)
                return;

            var json = JsonSerializer.Serialize(new
            {
                @event = eventType,
                createdAt = DateTime.UtcNow,
                data = payload,
            });

            if (json.Length > WebhookDelivery.PayloadMaxLength)
            {
                logger.LogWarning("Webhook payload for {Event} exceeded {Max} bytes and was dropped",
                    eventType, WebhookDelivery.PayloadMaxLength);
                return;
            }

            foreach (var endpoint in endpoints)
            {
                if (!WebhookEvents.Includes(endpoint.Events, eventType))
                    continue;

                db.WebhookDeliveries.Add(new WebhookDelivery
                {
                    Id = Guid.CreateVersion7(),
                    EndpointId = endpoint.Id,
                    UserId = userId.Value,
                    EventType = eventType,
                    PayloadJson = json,
                    // Due immediately; the job picks it up on its next pass.
                    NextAttemptAt = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue webhook {Event}", eventType);
        }
    }

    /// <summary>
    /// Attempts one delivery and records the outcome.
    ///
    /// Returns true when it succeeded. A 2xx is success; anything else — including a timeout or a
    /// DNS failure — is a retryable failure until the attempt budget runs out.
    /// </summary>
    public async Task<bool> AttemptAsync(WebhookDelivery delivery, WebhookEndpoint endpoint, CancellationToken ct)
    {
        delivery.AttemptCount++;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ApiKeyAuthenticator.Sign(endpoint.Secret, timestamp, delivery.PayloadJson);

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = SendTimeout;

            using var content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url) { Content = content };

            request.Headers.TryAddWithoutValidation(SignatureHeader, signature);
            request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation(EventHeader, delivery.EventType);
            request.Headers.TryAddWithoutValidation(DeliveryHeader, delivery.Id.ToString());
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("QuickShare-Webhooks", "1.0"));

            using var response = await client.SendAsync(request, ct);

            delivery.ResponseStatusCode = (int)response.StatusCode;
            delivery.ResponseBody = Truncate(await ReadBodyAsync(response, ct));

            if (response.IsSuccessStatusCode)
            {
                Succeed(delivery, endpoint);
                return true;
            }

            Fail(delivery, endpoint, $"HTTP {(int)response.StatusCode}");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            delivery.ResponseBody = Truncate(ex.Message);
            Fail(delivery, endpoint, ex.Message);
            return false;
        }
    }

    private static void Succeed(WebhookDelivery delivery, WebhookEndpoint endpoint)
    {
        delivery.Status = WebhookDeliveryStatus.Succeeded;
        delivery.NextAttemptAt = null;
        delivery.CompletedAt = DateTime.UtcNow;

        // One success clears the failure streak: an endpoint that came back is not on its way to
        // being parked, and carrying old failures forward would park a working integration.
        endpoint.ConsecutiveFailures = 0;
        endpoint.LastError = null;
        endpoint.LastDeliveredAt = DateTime.UtcNow;
    }

    private static void Fail(WebhookDelivery delivery, WebhookEndpoint endpoint, string error)
    {
        endpoint.ConsecutiveFailures++;
        endpoint.LastError = Truncate(error, WebhookEndpoint.ErrorMaxLength);

        if (delivery.AttemptCount >= WebhookDelivery.MaxAttempts)
        {
            // Given up on, but kept: a failed delivery is the thing an integrator wants to replay
            // once they have fixed their end (issue #288).
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.NextAttemptAt = null;
            delivery.CompletedAt = DateTime.UtcNow;
            return;
        }

        delivery.NextAttemptAt = DateTime.UtcNow.Add(WebhookDelivery.BackoffFor(delivery.AttemptCount));
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return string.Empty;
        }
    }

    private static string? Truncate(string? value, int max = WebhookDelivery.ResponseMaxLength) =>
        string.IsNullOrEmpty(value) ? value
            : value.Length <= max ? value
            : value[..max];
}
