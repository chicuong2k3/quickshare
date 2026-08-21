using System.Text.Json;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Channels;

public sealed record ChannelHookDto(
    string Kind,
    bool Enabled,
    bool NotifyOnDelivery,
    bool NotifyOnComment,
    bool HasUrl,
    bool IsParked,
    DateTime? LastPostedAt,
    int ConsecutiveFailures,
    string? LastError);

public sealed class ChannelHookRequest
{
    public string? Kind { get; set; }
    /// <summary>Blank leaves the stored webhook alone; a value replaces it.</summary>
    public string? WebhookUrl { get; set; }
    public bool Enabled { get; set; } = true;
    public bool NotifyOnDelivery { get; set; } = true;
    public bool NotifyOnComment { get; set; } = true;
}

/// <summary>GET /api/workspace/channel — the workspace's chat integration (issue #180).</summary>
public sealed class GetChannelHookEndpoint : EndpointWithoutRequest<ChannelHookDto?>
{
    public override void Configure()
    {
        Get("/api/workspace/channel");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Get the workspace's Slack/Teams webhook configuration";
            s.Description = "Workspace admins only. The webhook URL is never returned — it is the credential.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var hook = await db.WorkspaceChannelHooks
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.WorkspaceId == seat.WorkspaceId, ct);

        await Send.OkAsync(hook is null ? null : Map(hook), ct);
    }

    /// <summary>
    /// The URL is reported as present, never returned. Anyone holding it can post into the
    /// channel, so it is a credential and reading it back is a support conversation, not an API.
    /// </summary>
    internal static ChannelHookDto Map(WorkspaceChannelHook hook) => new(
        hook.Kind,
        hook.Enabled,
        hook.NotifyOnDelivery,
        hook.NotifyOnComment,
        !string.IsNullOrEmpty(hook.WebhookUrl),
        hook.IsParked,
        hook.LastPostedAt,
        hook.ConsecutiveFailures,
        hook.LastError);
}

/// <summary>
/// PUT /api/workspace/channel — point the workspace at a Slack or Teams channel (issue #180).
///
/// HTTPS only, and saving clears a parked hook's failure count: fixing a revoked webhook and
/// pressing save is the obvious gesture, and a separate "resume" control would be ceremony.
/// </summary>
public sealed class UpdateChannelHookEndpoint : Endpoint<ChannelHookRequest, ChannelHookDto>
{
    public override void Configure()
    {
        Put("/api/workspace/channel");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Configure Slack or Teams posting";
            s.Description = "Workspace admins only. Accepts a Slack incoming-webhook or Teams connector URL over HTTPS. Posts are best-effort and never block a share.";
        });
    }

    public override async Task HandleAsync(ChannelHookRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var kind = (req.Kind ?? ChannelKind.Slack).Trim().ToLowerInvariant();
        if (!ChannelKind.All.Contains(kind))
        {
            ThrowError(ErrorCodes.ChannelKindInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var hook = await db.WorkspaceChannelHooks
            .FirstOrDefaultAsync(h => h.WorkspaceId == seat.WorkspaceId, ct);

        var url = (req.WebhookUrl ?? string.Empty).Trim();
        if (url.Length > 0)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || parsed.Scheme != Uri.UriSchemeHttps
                || url.Length > WorkspaceChannelHook.UrlMaxLength)
            {
                ThrowError(ErrorCodes.ChannelUrlInvalid, StatusCodes.Status400BadRequest);
                return;
            }
        }
        else if (hook is null)
        {
            // A first save with no URL would create a hook that can never post.
            ThrowError(ErrorCodes.ChannelUrlInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (hook is null)
        {
            hook = new WorkspaceChannelHook
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                CreatedAt = DateTime.UtcNow,
            };
            db.WorkspaceChannelHooks.Add(hook);
        }

        hook.Kind = kind;
        hook.Enabled = req.Enabled;
        hook.NotifyOnDelivery = req.NotifyOnDelivery;
        hook.NotifyOnComment = req.NotifyOnComment;
        hook.ConsecutiveFailures = 0;
        hook.LastError = null;
        hook.UpdatedAt = DateTime.UtcNow;

        if (url.Length > 0)
            hook.WebhookUrl = url;

        await db.SaveChangesAsync(ct);

        // The host, not the full URL: an audit trail that records the credential is a second
        // place the credential lives.
        var host = Uri.TryCreate(hook.WebhookUrl, UriKind.Absolute, out var stored) ? stored.Host : string.Empty;
        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.ChannelHookChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { kind, host, hook.Enabled }));

        await Send.OkAsync(GetChannelHookEndpoint.Map(hook), ct);
    }
}

/// <summary>DELETE /api/workspace/channel — stop posting and forget the webhook (issue #180).</summary>
public sealed class DeleteChannelHookEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/channel");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Remove the workspace's channel webhook");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var hook = await db.WorkspaceChannelHooks
            .FirstOrDefaultAsync(h => h.WorkspaceId == seat.WorkspaceId, ct);

        if (hook is null)
        {
            ThrowError(ErrorCodes.ChannelNotFound, StatusCodes.Status404NotFound);
            return;
        }

        db.WorkspaceChannelHooks.Remove(hook);
        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.ChannelHookChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { removed = true }));

        await Send.NoContentAsync(ct);
    }
}
