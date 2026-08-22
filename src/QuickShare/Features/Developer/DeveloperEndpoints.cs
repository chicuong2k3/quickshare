using System.Security.Cryptography;
using QuickShare.Extensions;
using QuickShare.Features.Developer.ApiKeys;
using QuickShare.Features.Developer.Webhooks;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Developer;

/// <summary>An API key. <c>Key</c> is populated exactly once, in the response that creates it.</summary>
public sealed record ApiKeyDto(
    Guid Id, string Name, string Prefix, string Scopes,
    DateTime CreatedAt, DateTime? ExpiresAt, DateTime? LastUsedAt, string? Key);

/// <summary>A webhook subscription. The secret appears only when it is created or rotated.</summary>
public sealed record WebhookEndpointDto(
    Guid Id, string Url, string Events, string? Description, bool Enabled,
    int ConsecutiveFailures, string? LastError, DateTime? LastDeliveredAt, bool IsParked, string? Secret);

/// <summary>One delivery attempt, for the event log (issue #223).</summary>
public sealed record WebhookDeliveryDto(
    Guid Id, Guid EndpointId, string EventType, string Status, int AttemptCount,
    int? ResponseStatusCode, string? ResponseBody, DateTime CreatedAt, DateTime? CompletedAt);

public sealed class CreateApiKeyRequest
{
    public string? Name { get; set; }
    public string? Scopes { get; set; }
    public int? ExpiresInDays { get; set; }
}

public sealed class WebhookEndpointRequest
{
    public string? Url { get; set; }
    public string? Events { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>GET /api/developer/keys — the caller's API keys (issue #115).</summary>
public sealed class ListApiKeysEndpoint : EndpointWithoutRequest<IReadOnlyList<ApiKeyDto>>
{
    public override void Configure()
    {
        Get("/api/developer/keys");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s =>
        {
            s.Summary = "List API keys";
            s.Description = "The key value itself is never returned — only the prefix, which identifies it without being usable.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var keys = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == user.Id && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix, k.Scopes, k.CreatedAt, k.ExpiresAt, k.LastUsedAt, null))
            .ToListAsync(ct);

        await Send.OkAsync(keys, ct);
    }
}

/// <summary>
/// POST /api/developer/keys — mint a key (issue #115).
///
/// The raw value comes back once. There is no endpoint that can read it again, and that is the
/// point: a credential a support agent can retrieve is a credential an attacker can social-
/// engineer. Losing it means minting another, which costs a click.
/// </summary>
public sealed class CreateApiKeyEndpoint : Endpoint<CreateApiKeyRequest, ApiKeyDto>
{
    private const int MaxKeysPerAccount = 20;
    private const int MaxExpiryDays = 3650;

    public override void Configure()
    {
        Post("/api/developer/keys");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s =>
        {
            s.Summary = "Create an API key";
            s.Description = "Returns the key once. Store it now — it cannot be read back.";
        });
    }

    public override async Task HandleAsync(CreateApiKeyRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var live = await db.ApiKeys.CountAsync(k => k.UserId == user.Id && k.RevokedAt == null, ct);
        if (live >= MaxKeysPerAccount)
        {
            ThrowError(ErrorCodes.ApiKeyLimitReached, StatusCodes.Status409Conflict);
            return;
        }

        if (req.ExpiresInDays is { } days && (days <= 0 || days > MaxExpiryDays))
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
            return;
        }

        var (raw, hash, prefix) = ApiKeyAuthenticator.Generate();
        var name = string.IsNullOrWhiteSpace(req.Name)
            ? "API key"
            : req.Name.Trim()[..Math.Min(req.Name.Trim().Length, ApiKey.NameMaxLength)];

        var key = new ApiKey
        {
            UserId = user.Id,
            Name = name,
            KeyHash = hash,
            Prefix = prefix,
            Scopes = ApiScopes.Normalize(req.Scopes),
            ExpiresAt = req.ExpiresInDays is { } d ? DateTime.UtcNow.AddDays(d) : null,
        };

        db.ApiKeys.Add(key);

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.ApiKeyCreated,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new ApiKeyDto(
            key.Id, key.Name, key.Prefix, key.Scopes, key.CreatedAt, key.ExpiresAt, null, raw), ct);
    }
}

/// <summary>DELETE /api/developer/keys/{Id} — revoke a key (issue #115).</summary>
public sealed class RevokeApiKeyEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/developer/keys/{Id}");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s => s.Summary = "Revoke an API key");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        // Scoped to the caller, never by id alone.
        var key = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == Route<Guid>("Id") && k.UserId == user.Id && k.RevokedAt == null, ct);

        if (key is null)
        {
            ThrowError(ErrorCodes.ApiKeyNotFound, StatusCodes.Status404NotFound);
            return;
        }

        key.RevokedAt = DateTime.UtcNow;

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.ApiKeyRevoked,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>GET /api/developer/webhooks — the caller's subscriptions (issue #39).</summary>
public sealed class ListWebhooksEndpoint : EndpointWithoutRequest<IReadOnlyList<WebhookEndpointDto>>
{
    public override void Configure()
    {
        Get("/api/developer/webhooks");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s => s.Summary = "List webhook endpoints");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var endpoints = await db.WebhookEndpoints
            .AsNoTracking()
            .Where(e => e.UserId == user.Id && e.RevokedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        await Send.OkAsync((IReadOnlyList<WebhookEndpointDto>)[.. endpoints.Select(e => Map(e, null))], ct);
    }

    /// <summary>The secret is reported as present, never returned except at creation.</summary>
    internal static WebhookEndpointDto Map(WebhookEndpoint endpoint, string? secret) => new(
        endpoint.Id, endpoint.Url, endpoint.Events, endpoint.Description, endpoint.Enabled,
        endpoint.ConsecutiveFailures, endpoint.LastError, endpoint.LastDeliveredAt, endpoint.IsParked, secret);
}

/// <summary>
/// POST /api/developer/webhooks — subscribe an endpoint (issues #39, #172).
///
/// HTTPS only. A signed payload sent over plaintext is a signed payload anybody on the path can
/// read, and the events carry file names and recipient counts.
/// </summary>
public sealed class CreateWebhookEndpoint : Endpoint<WebhookEndpointRequest, WebhookEndpointDto>
{
    private const int MaxEndpointsPerAccount = 10;

    public override void Configure()
    {
        Post("/api/developer/webhooks");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s =>
        {
            s.Summary = "Create a webhook endpoint";
            s.Description = "Returns the signing secret once. Verify X-QuickShare-Signature as HMAC-SHA256 over {timestamp}.{body}.";
        });
    }

    public override async Task HandleAsync(WebhookEndpointRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || req.Url!.Length > WebhookEndpoint.UrlMaxLength)
        {
            ThrowError(ErrorCodes.WebhookUrlInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var live = await db.WebhookEndpoints.CountAsync(e => e.UserId == user.Id && e.RevokedAt == null, ct);
        if (live >= MaxEndpointsPerAccount)
        {
            ThrowError(ErrorCodes.WebhookLimitReached, StatusCodes.Status409Conflict);
            return;
        }

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(WebhookEndpoint.SecretLength / 2)).ToLowerInvariant();

        var endpoint = new WebhookEndpoint
        {
            UserId = user.Id,
            Url = req.Url.Trim(),
            Secret = secret,
            Events = WebhookEvents.Normalize(req.Events),
            Description = string.IsNullOrWhiteSpace(req.Description)
                ? null
                : req.Description.Trim()[..Math.Min(req.Description.Trim().Length, WebhookEndpoint.DescriptionMaxLength)],
            Enabled = req.Enabled,
        };

        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(ListWebhooksEndpoint.Map(endpoint, secret), ct);
    }
}

/// <summary>
/// PATCH /api/developer/webhooks/{Id} — change or un-park a subscription (issues #39, #172).
///
/// Saving clears the failure count, because fixing a broken receiver and pressing save is the
/// obvious gesture and a separate "resume" control would be ceremony. Same reasoning as the
/// channel hook (#180).
/// </summary>
public sealed class UpdateWebhookEndpoint : Endpoint<WebhookEndpointRequest, WebhookEndpointDto>
{
    public override void Configure()
    {
        Patch("/api/developer/webhooks/{Id}");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s => s.Summary = "Update a webhook endpoint (also clears a parked failure count)");
    }

    public override async Task HandleAsync(WebhookEndpointRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var endpoint = await db.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == Route<Guid>("Id") && e.UserId == user.Id && e.RevokedAt == null, ct);

        if (endpoint is null)
        {
            ThrowError(ErrorCodes.WebhookNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.Url))
        {
            if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            {
                ThrowError(ErrorCodes.WebhookUrlInvalid, StatusCodes.Status400BadRequest);
                return;
            }
            endpoint.Url = req.Url.Trim();
        }

        if (!string.IsNullOrWhiteSpace(req.Events))
            endpoint.Events = WebhookEvents.Normalize(req.Events);

        endpoint.Enabled = req.Enabled;
        endpoint.ConsecutiveFailures = 0;
        endpoint.LastError = null;

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(ListWebhooksEndpoint.Map(endpoint, null), ct);
    }
}

/// <summary>DELETE /api/developer/webhooks/{Id} — unsubscribe (issue #39).</summary>
public sealed class DeleteWebhookEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/developer/webhooks/{Id}");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s => s.Summary = "Remove a webhook endpoint");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var endpoint = await db.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == Route<Guid>("Id") && e.UserId == user.Id && e.RevokedAt == null, ct);

        if (endpoint is null)
        {
            ThrowError(ErrorCodes.WebhookNotFound, StatusCodes.Status404NotFound);
            return;
        }

        endpoint.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// POST /api/developer/webhooks/{Id}/test — send a sample event now (issue #223).
///
/// The test console. Delivered through the same queue and the same signing as a real event, so
/// what it proves is what the integrator needs proved: that their receiver accepts what we
/// actually send. A special-cased test path would prove only that the test path works.
/// </summary>
public sealed class TestWebhookEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/developer/webhooks/{Id}/test");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s => s.Summary = "Queue a sample event to this endpoint");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var endpoint = await db.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == Route<Guid>("Id") && e.UserId == user.Id && e.RevokedAt == null, ct);

        if (endpoint is null)
        {
            ThrowError(ErrorCodes.WebhookNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await Resolve<WebhookDispatcher>().EnqueueAsync(user.Id, WebhookEvents.DeliveryCreated, new
        {
            test = true,
            shareCode = "test-share-code",
            fileName = "example.pdf",
            fileSize = 1024,
        }, ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>GET /api/developer/webhooks/deliveries — the event log (issue #223).</summary>
public sealed class ListWebhookDeliveriesEndpoint : EndpointWithoutRequest<IReadOnlyList<WebhookDeliveryDto>>
{
    private const int PageSize = 50;

    public override void Configure()
    {
        Get("/api/developer/webhooks/deliveries");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s => s.Summary = "Recent webhook delivery attempts, newest first");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var deliveries = await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.UserId == user.Id)
            .OrderByDescending(d => d.CreatedAt)
            .Take(PageSize)
            .Select(d => new WebhookDeliveryDto(
                d.Id, d.EndpointId, d.EventType, d.Status, d.AttemptCount,
                d.ResponseStatusCode, d.ResponseBody, d.CreatedAt, d.CompletedAt))
            .ToListAsync(ct);

        await Send.OkAsync(deliveries, ct);
    }
}

/// <summary>
/// POST /api/developer/webhooks/deliveries/{Id}/replay — send it again (issue #288).
///
/// Replays the original payload rather than regenerating it. Regenerating would send today's
/// view of the delivery, which is not what failed and not what the integrator is debugging —
/// and for an expired or deleted share there would be nothing to regenerate from at all.
/// </summary>
public sealed class ReplayWebhookDeliveryEndpoint : EndpointWithoutRequest<WebhookDeliveryDto>
{
    public override void Configure()
    {
        Post("/api/developer/webhooks/deliveries/{Id}/replay");
        Description(b => b.WithTags(ApiTags.Developer));
        Summary(s =>
        {
            s.Summary = "Replay a webhook delivery";
            s.Description = "Queues a fresh attempt carrying the original payload. The original row is kept.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var original = await db.WebhookDeliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == Route<Guid>("Id") && d.UserId == user.Id, ct);

        if (original is null)
        {
            ThrowError(ErrorCodes.WebhookDeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var endpointLive = await db.WebhookEndpoints
            .AnyAsync(e => e.Id == original.EndpointId && e.RevokedAt == null, ct);

        if (!endpointLive)
        {
            ThrowError(ErrorCodes.WebhookNotFound, StatusCodes.Status404NotFound);
            return;
        }

        // A new row, not a reset of the old one: the log is a record of what happened, and
        // rewriting history to hide a failure is exactly what a debugging log must not do.
        var replay = new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            EndpointId = original.EndpointId,
            UserId = original.UserId,
            EventType = original.EventType,
            PayloadJson = original.PayloadJson,
            NextAttemptAt = DateTime.UtcNow,
        };

        db.WebhookDeliveries.Add(replay);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new WebhookDeliveryDto(
            replay.Id, replay.EndpointId, replay.EventType, replay.Status, 0, null, null, replay.CreatedAt, null), ct);
    }
}
