using System.Text.Json;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Streaming;

public sealed record SiemSinkDto(
    string Endpoint,
    string Format,
    string EventFilter,
    bool Enabled,
    bool HasSecret,
    bool IsParked,
    DateTime? LastShippedAt,
    DateTime? LastAttemptAt,
    int ConsecutiveFailures,
    string? LastError);

public sealed class SiemSinkRequest
{
    public string? Endpoint { get; set; }
    public string? Format { get; set; }
    /// <summary>Blank leaves the stored secret alone; a value replaces it.</summary>
    public string? Secret { get; set; }
    public string? EventFilter { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>GET /api/workspace/streaming — the workspace's SIEM sink and its health (issue #191).</summary>
public sealed class GetSiemSinkEndpoint : EndpointWithoutRequest<SiemSinkDto?>
{
    public override void Configure()
    {
        Get("/api/workspace/streaming");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Get the workspace's audit-streaming configuration";
            s.Description = "Workspace admins only. Includes shipping health: last delivered event, consecutive failures and the last error.";
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

        var sink = await db.SiemSinks.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == seat.WorkspaceId, ct);

        await Send.OkAsync(sink is null ? null : Map(sink), ct);
    }

    /// <summary>
    /// The secret is never returned, only its presence. An admin who wants to rotate it can set
    /// a new one; an admin who wants to read it back is a support ticket, not an API.
    /// </summary>
    internal static SiemSinkDto Map(SiemSink sink) => new(
        sink.Endpoint,
        sink.Format,
        sink.EventFilter,
        sink.Enabled,
        !string.IsNullOrEmpty(sink.Secret),
        sink.IsParked,
        sink.LastShippedAt,
        sink.LastAttemptAt,
        sink.ConsecutiveFailures,
        sink.LastError);
}

/// <summary>
/// PUT /api/workspace/streaming — point the audit trail at a SIEM (issue #191).
///
/// Saving resets the failure counter, which is how a parked sink is un-parked: fixing the
/// endpoint and pressing save is the obvious gesture, and making the admin hunt for a separate
/// "resume" button would just be ceremony.
/// </summary>
public sealed class UpdateSiemSinkEndpoint : Endpoint<SiemSinkRequest, SiemSinkDto>
{
    public override void Configure()
    {
        Put("/api/workspace/streaming");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Configure audit-log streaming to a SIEM";
            s.Description = "Workspace admins only. HTTPS endpoints only. Formats: json, cef, splunk_hec. Saving clears a parked sink's failure count.";
        });
    }

    public override async Task HandleAsync(SiemSinkRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var endpoint = (req.Endpoint ?? string.Empty).Trim();
        // HTTPS only: an audit stream over plaintext is an audit stream someone else can read,
        // which inverts the point of shipping it to a SOC in the first place.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || endpoint.Length > SiemSink.EndpointMaxLength)
        {
            ThrowError(ErrorCodes.SinkEndpointInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var format = (req.Format ?? SiemFormat.Json).Trim().ToLowerInvariant();
        if (!SiemFormat.All.Contains(format))
        {
            ThrowError(ErrorCodes.SinkFormatInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var sink = await db.SiemSinks.FirstOrDefaultAsync(s => s.WorkspaceId == seat.WorkspaceId, ct);
        var isNew = sink is null;

        if (sink is null)
        {
            sink = new SiemSink
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                CreatedAt = DateTime.UtcNow,
                // A new sink starts at "now": back-filling months of history into somebody's
                // SIEM on first save is a bill they did not agree to.
                LastShippedAt = DateTime.UtcNow,
            };
            db.SiemSinks.Add(sink);
        }

        sink.Endpoint = endpoint;
        sink.Format = format;
        sink.EventFilter = Truncate(req.EventFilter, SiemSink.EventFilterMaxLength);
        sink.Enabled = req.Enabled;
        sink.ConsecutiveFailures = 0;
        sink.LastError = null;
        sink.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(req.Secret))
            sink.Secret = Truncate(req.Secret, SiemSink.SecretMaxLength);

        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value, SecurityEventType.SiemSinkChanged,
            HttpContext, ct, JsonSerializer.Serialize(new { endpoint = uri.Host, format, created = isNew }));

        await Send.OkAsync(GetSiemSinkEndpoint.Map(sink), ct);
    }

    private static string Truncate(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

/// <summary>DELETE /api/workspace/streaming — stop streaming and forget the endpoint (issue #191).</summary>
public sealed class DeleteSiemSinkEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/streaming");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Remove the workspace's audit-streaming configuration");
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

        var sink = await db.SiemSinks.FirstOrDefaultAsync(s => s.WorkspaceId == seat.WorkspaceId, ct);
        if (sink is null)
        {
            ThrowError(ErrorCodes.SinkNotFound, StatusCodes.Status404NotFound);
            return;
        }

        db.SiemSinks.Remove(sink);
        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value, SecurityEventType.SiemSinkChanged,
            HttpContext, ct, JsonSerializer.Serialize(new { removed = true }));

        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// Status codes for streaming routes, folded into the workspace map so one route never answers
/// 403 where the next answers 400 for the same class of refusal.
/// </summary>
internal static class StreamingStatus
{
    public static int For(string code) => code switch
    {
        ErrorCodes.SinkNotFound => StatusCodes.Status404NotFound,
        _ => WorkspaceStatus.For(code),
    };
}
