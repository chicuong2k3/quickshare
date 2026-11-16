using System.Text;
using QuickShare.Extensions;
using QuickShare.Features.Deliveries.LegalHold;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Audit;

public sealed record WorkspaceAuditRow(
    Guid Id,
    Guid ActorId,
    string ActorName,
    string ActorEmail,
    string Action,
    string? MetadataJson,
    DateTime CreatedAt);

public sealed record WorkspaceAuditPage(IReadOnlyList<WorkspaceAuditRow> Rows, int Total);

/// <summary>
/// Reads the workspace slice of the append-only security store (issue #145). Admin-only:
/// the member lens already exists on the personal timeline, and this one exposes what other
/// people did. Filters are applied in SQL so a large workspace does not page through
/// everything to find one member's week.
/// </summary>
public sealed class WorkspaceAuditReader(AppDbContext db)
{
    public const int MaxPageSize = 200;
    /// <summary>Matches the 90-day SecurityEventCleanupJob window — older rows are gone, not hidden.</summary>
    public const int RetentionDays = 90;

    public async Task<WorkspaceAuditPage> ReadAsync(
        Guid workspaceId,
        Guid? actorId,
        SecurityEventType? action,
        DateTime? from,
        DateTime? to,
        int take,
        CancellationToken ct)
    {
        var query = db.SecurityEvents
            .AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId);

        if (actorId is { } id)
            query = query.Where(e => e.UserId == id);
        if (action is { } type)
            query = query.Where(e => e.EventType == type);
        if (from is { } start)
            query = query.Where(e => e.CreatedAt >= start);
        if (to is { } end)
            query = query.Where(e => e.CreatedAt <= end);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(take, 1, MaxPageSize))
            .Select(e => new WorkspaceAuditRow(
                e.Id,
                e.UserId,
                db.Users.Where(u => u.Id == e.UserId).Select(u => u.Name).FirstOrDefault() ?? string.Empty,
                db.Users.Where(u => u.Id == e.UserId).Select(u => u.Email).FirstOrDefault() ?? string.Empty,
                e.EventType.ToString().ToLowerInvariant(),
                e.MetadataJson,
                e.CreatedAt))
            .ToListAsync(ct);

        return new WorkspaceAuditPage(rows, total);
    }

    /// <summary>
    /// Resolves the caller's admin seat, or null when they may not read the log. Both the
    /// JSON and CSV routes go through this — one gate, so they cannot disagree about who
    /// is an admin.
    /// </summary>
    public static async Task<WorkspaceMember?> ResolveAdminSeatAsync(
        WorkspaceService workspaces, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var user = await http.User.GetUserAsync(db, ct);
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        return seat?.CanAdminister == true ? seat : null;
    }

    internal static SecurityEventType? ParseAction(string? value)
        => Enum.TryParse<SecurityEventType>(value, ignoreCase: true, out var parsed) ? parsed : null;

    internal static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}

public sealed class WorkspaceAuditRequest
{
    public Guid? MemberId { get; set; }
    public string? Action { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public int Take { get; set; } = 50;
}

/// <summary>GET /api/workspace/audit — the workspace admin's accountability view (issue #145).</summary>
public sealed class WorkspaceAuditEndpoint : Endpoint<WorkspaceAuditRequest, WorkspaceAuditPage>
{
    public override void Configure()
    {
        Get("/api/workspace/audit");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "List workspace audit events";
            s.Description = "Workspace admins only. Immutable, append-only, filterable by member, action and date. Retained 90 days.";
        });
    }

    public override async Task HandleAsync(WorkspaceAuditRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(
            Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var page = await new WorkspaceAuditReader(db).ReadAsync(
            seat.WorkspaceId,
            req.MemberId,
            WorkspaceAuditReader.ParseAction(req.Action),
            WorkspaceAuditReader.ParseDate(req.From),
            WorkspaceAuditReader.ParseDate(req.To),
            req.Take,
            ct);

        await Send.OkAsync(page, ct);
    }
}

/// <summary>GET /api/workspace/audit/export — same filters, CSV out (issues #145/#74).</summary>
public sealed class WorkspaceAuditExportEndpoint : Endpoint<WorkspaceAuditRequest>
{
    private const string CsvHeader = "timestamp_utc,actor_name,actor_email,action,metadata";

    public override void Configure()
    {
        Get("/api/workspace/audit/export");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Export workspace audit events as CSV";
            s.Description = "Workspace admins only. Honours the same member/action/date filters as the list view.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(WorkspaceAuditRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(
            Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var page = await new WorkspaceAuditReader(db).ReadAsync(
            seat.WorkspaceId,
            req.MemberId,
            WorkspaceAuditReader.ParseAction(req.Action),
            WorkspaceAuditReader.ParseDate(req.From),
            WorkspaceAuditReader.ParseDate(req.To),
            WorkspaceAuditReader.MaxPageSize,
            ct);

        var csv = new StringBuilder();
        csv.AppendLine(CsvHeader);

        foreach (var row in page.Rows)
        {
            csv.Append(row.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.ActorName));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.ActorEmail));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.Action));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.MetadataJson ?? string.Empty));
            csv.AppendLine();
        }

        var filename = $"quickshare-workspace-audit-{DateTime.UtcNow:yyyyMMdd}.csv";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        await Send.StringAsync(csv.ToString(), StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
    }

    /// <summary>Names and JSON metadata are free text — always quote, double embedded quotes.</summary>
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
