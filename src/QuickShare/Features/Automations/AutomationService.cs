using System.Security.Cryptography;
using System.Text;

namespace QuickShare.Features.Automations;

public sealed record AutomationConnectionDto(
    Guid Id,
    string Platform,
    string Name,
    string Scopes,
    string TokenPrefix,
    Guid? WebhookEndpointId,
    string? WebhookUrl,
    string? Events,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    string?IssuedToken = null,
    string? IssuedWebhookSecret = null);

public sealed record AutomationTemplateItem(
    string Id,
    string Platform,
    string Title,
    string TriggerEvent,
    string ActionName,
    string Description);

public sealed record AutomationCatalogResponse(
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Actions,
    IReadOnlyList<AutomationTemplateItem> Templates);

public sealed class CreateAutomationConnectionRequest
{
    public string? Platform { get; set; }
    public string? Name { get; set; }
    public string? Scopes { get; set; }
    public string? WebhookUrl { get; set; }
    public string? Events { get; set; }
}

public sealed class GetAutomationCatalogEndpoint : EndpointWithoutRequest<AutomationCatalogResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.AutomationsCatalogApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Get Zapier & Make no-code triggers, actions, and workflow templates");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var templates = new List<AutomationTemplateItem>
        {
            new("zap-sheets-log", AutomationConnection.PlatformZapier, "Log new QuickShare transfers to Google Sheets", WebhookEvents.DeliveryCreated, "google_sheets.append_row", "Every time a file is uploaded, append a row with the share link, size, and expiry date."),
            new("zap-slack-download", AutomationConnection.PlatformZapier, "Post Slack alert on first recipient download", WebhookEvents.DeliveryDownloaded, "slack.post_message", "Notify your #client-deliveries channel when a recipient downloads a contract."),
            new("make-drive-archive", AutomationConnection.PlatformMake, "Archive expiring transfers via Make.com", WebhookEvents.DeliveryExpired, "http.archive_notice", "Trigger a Make scenario when a delivery expires to follow up with the client."),
        };

        var actions = new[] { "delivery.create", "delivery.get_link", "delivery.revoke", "analytics.summary" };
        await Send.OkAsync(new AutomationCatalogResponse(WebhookEvents.All, actions, templates), ct);
    }
}

public sealed class ListAutomationConnectionsEndpoint : EndpointWithoutRequest<IReadOnlyList<AutomationConnectionDto>>
{
    public override void Configure()
    {
        Get(RoutePaths.AutomationsApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "List connected Zapier & Make automation apps");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var rows = await db.AutomationConnections
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var webhookIds = rows.Where(r => r.WebhookEndpointId != null).Select(r => r.WebhookEndpointId!.Value).ToList();
        var webhooks = await db.WebhookEndpoints
            .AsNoTracking()
            .Where(w => webhookIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, ct);

        var dtos = rows.Select(r =>
        {
            webhooks.TryGetValue(r.WebhookEndpointId ?? Guid.Empty, out var wh);
            return new AutomationConnectionDto(
                r.Id,
                r.Platform,
                r.Name,
                r.Scopes,
                r.TokenPrefixValue,
                r.WebhookEndpointId,
                wh?.Url,
                wh?.Events,
                r.IsActive,
                r.CreatedAt,
                r.RevokedAt);
        }).ToList();

        await Send.OkAsync(dtos, ct);
    }
}

public sealed class CreateAutomationConnectionEndpoint : Endpoint<CreateAutomationConnectionRequest, AutomationConnectionDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AutomationsApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Connect a Zapier or Make automation with scoped API token and optional trigger webhook");
    }

    public override async Task HandleAsync(CreateAutomationConnectionRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name) || name.Length > AutomationConnection.NameMaxLength)
        {
            ThrowError(ErrorCodes.AutomationNameInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var platform = (req.Platform ?? AutomationConnection.PlatformZapier).Trim().ToLowerInvariant();
        if (platform is not (AutomationConnection.PlatformZapier or AutomationConnection.PlatformMake or AutomationConnection.PlatformCustom))
        {
            platform = AutomationConnection.PlatformZapier;
        }

        var scopes = ApiScopes.Normalize(req.Scopes);
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        Guid? webhookId = null;
        string? webhookUrl = null;
        string? webhookEvents = null;
        string? webhookSecret = null;

        if (!string.IsNullOrWhiteSpace(req.WebhookUrl))
        {
            var trimmedUrl = req.WebhookUrl.Trim();
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                ThrowError(ErrorCodes.WebhookUrlInvalid, StatusCodes.Status400BadRequest);
                return;
            }

            webhookUrl = trimmedUrl;
            webhookEvents = WebhookEvents.Normalize(req.Events);
            webhookSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(WebhookEndpoint.SecretLength / 2)).ToLowerInvariant();

            var wh = new WebhookEndpoint
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                Url = webhookUrl,
                Secret = webhookSecret,
                Events = webhookEvents,
                Description = $"{platform.ToUpperInvariant()}: {name}",
                Enabled = true,
            };
            db.WebhookEndpoints.Add(wh);
            webhookId = wh.Id;
        }

        var rawToken = ApiKey.TokenPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(ApiKey.SecretBytes)).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        var prefix = rawToken[..ApiKey.PrefixLength];

        // Mint an underlying scoped ApiKey so all /api/v1/* endpoints accept the automation token out-of-the-box.
        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Name = $"[{platform}] {name}"[..Math.Min($"[{platform}] {name}".Length, ApiKey.NameMaxLength)],
            KeyHash = tokenHash,
            Prefix = prefix,
            Scopes = scopes,
        });

        var workspaceId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var conn = new AutomationConnection
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = workspaceId,
            Platform = platform,
            Name = name,
            Scopes = scopes,
            TokenPrefixValue = prefix,
            TokenHash = tokenHash,
            WebhookEndpointId = webhookId,
        };
        db.AutomationConnections.Add(conn);

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = workspaceId,
            EventType = SecurityEventType.AutomationConnected,
            MetadataJson = $"{{\"platform\":\"{platform}\",\"name\":\"{name}\",\"scopes\":\"{scopes}\"}}",
        });

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new AutomationConnectionDto(
            conn.Id,
            conn.Platform,
            conn.Name,
            conn.Scopes,
            conn.TokenPrefixValue,
            conn.WebhookEndpointId,
            webhookUrl,
            webhookEvents,
            conn.IsActive,
            conn.CreatedAt,
            conn.RevokedAt,
            rawToken,
            webhookSecret), ct);
    }
}

public sealed class RevokeAutomationConnectionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete($"{RoutePaths.AutomationsApi}/{{Id:guid}}");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Revoke a Zapier or Make automation connection and its underlying token/webhook");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var conn = await db.AutomationConnections
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.Id, ct);

        if (conn is null)
        {
            ThrowError(ErrorCodes.AutomationNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var now = DateTime.UtcNow;
        conn.RevokedAt = now;

        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.UserId == user.Id && k.KeyHash == conn.TokenHash, ct);
        if (apiKey is not null)
        {
            apiKey.RevokedAt = now;
        }

        if (conn.WebhookEndpointId is { } whId)
        {
            var wh = await db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == whId && w.UserId == user.Id, ct);
            if (wh is not null)
            {
                wh.RevokedAt = now;
                wh.Enabled = false;
            }
        }

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = conn.WorkspaceId,
            EventType = SecurityEventType.AutomationRevoked,
            MetadataJson = $"{{\"platform\":\"{conn.Platform}\",\"name\":\"{conn.Name}\"}}",
        });

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
