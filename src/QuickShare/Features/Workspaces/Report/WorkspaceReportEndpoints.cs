using System.Text;
using QuickShare.Features.Deliveries.LegalHold;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Report;

public sealed class WorkspaceReportRequest
{
    public int Days { get; set; } = WorkspaceReportService.DefaultWindowDays;
}

/// <summary>GET /api/workspace/report — workspace health at a glance (issue #300).</summary>
public sealed class WorkspaceReportEndpoint : Endpoint<WorkspaceReportRequest, WorkspaceReportDto>
{
    public override void Configure()
    {
        Get("/api/workspace/report");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Generate the workspace usage report";
            s.Description = "Workspace admins only. Seats, pooled quota, delivery volume, per-member usage, file types and inactive seats over a rolling window. Personal shares are excluded.";
        });
    }

    public override async Task HandleAsync(WorkspaceReportRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var report = await new WorkspaceReportService(db).BuildAsync(seat.WorkspaceId, req.Days, ct);
        if (report is null)
        {
            ThrowError(ErrorCodes.WorkspaceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        // Reading the org view is itself an admin action (#145) — an unaudited report is a
        // quiet way to read every member's volume.
        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceReportGenerated, HttpContext, ct);

        await Send.OkAsync(report, ct);
    }
}

/// <summary>GET /api/workspace/report/export — the same report as CSV (issues #300/#74).</summary>
public sealed class WorkspaceReportExportEndpoint : Endpoint<WorkspaceReportRequest>
{
    private const string CsvHeader = "member_name,member_email,role,deliveries,bytes_uploaded,downloads,last_activity_utc";

    public override void Configure()
    {
        Get("/api/workspace/report/export");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Export the workspace usage report as CSV";
            s.Description = "Workspace admins only. One row per member over the requested window.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(WorkspaceReportRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var report = await new WorkspaceReportService(db).BuildAsync(seat.WorkspaceId, req.Days, ct);
        if (report is null)
        {
            ThrowError(ErrorCodes.WorkspaceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceReportGenerated, HttpContext, ct);

        var csv = new StringBuilder();
        csv.AppendLine(CsvHeader);

        foreach (var member in report.Members)
        {
            csv.Append(Csv(member.Name));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(member.Email));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(member.Role));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(member.Deliveries.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(member.BytesUploaded.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(member.Downloads.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(member.LastActivityAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            csv.AppendLine();
        }

        var filename = $"quickshare-workspace-report-{DateTime.UtcNow:yyyyMMdd}.csv";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        await Send.StringAsync(csv.ToString(), StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
    }

    /// <summary>Names are free text — always quote, double embedded quotes.</summary>
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
