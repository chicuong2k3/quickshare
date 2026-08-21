using System.Text;
using QuickShare.Extensions;
using QuickShare.Features.Deliveries.LegalHold;
using QuickShare.Features.Payments;
using QuickShare.Features.Payments.Billing;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Seats;

public sealed record SeatMemberDto(
    string Email,
    string Name,
    string Role,
    string Status,
    DateTime? JoinedAt);

public sealed record SeatReportDto(
    string Plan,
    int Seats,
    int IncludedSeats,
    int BillableSeats,
    int ClaimedSeats,
    int FreeSeats,
    decimal SeatUnitAmount,
    decimal MonthlySeatAmount,
    decimal ProratedNextSeatAmount,
    IReadOnlyList<SeatMemberDto> Members);

/// <summary>
/// GET /api/workspace/seats — what the seats cost and who is in them (issue #278).
///
/// Any seat holder may read it. Seat pricing is not a secret from the people occupying the
/// seats, and a member who can see the workspace is going to ask why an invite was refused;
/// "3 of 3 claimed" answers that without a support ticket.
/// </summary>
public sealed class GetSeatReportEndpoint : EndpointWithoutRequest<SeatReportDto>
{
    private const string StatusActive = "active";
    private const string StatusPending = "pending";

    public override void Configure()
    {
        Get("/api/workspace/seats");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Seat usage and per-seat cost for the workspace";
            s.Description = "Included seats, purchased seats, claimed seats, and what one more would cost prorated to the end of the period.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var seat = await Resolve<WorkspaceService>().GetSeatAsync(user.Id, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.SeatsRequireWorkspace, StatusCodes.Status404NotFound);
            return;
        }

        var report = await BuildAsync(db, seat, ct);
        await Send.OkAsync(report, ct);
    }

    internal static async Task<SeatReportDto> BuildAsync(
        AppDbContext db, WorkspaceMember seat, CancellationToken ct)
    {
        var plan = seat.Workspace.Owner.Plan;
        var workspace = seat.Workspace;

        var rows = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspace.Id && m.RevokedAt == null)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.InviteEmail,
                m.Role,
                m.JoinedAt,
                Name = db.Users.Where(u => u.Id == m.UserId).Select(u => u.Name).FirstOrDefault() ?? string.Empty,
            })
            .ToListAsync(ct);

        // A pending invite holds a seat, so it counts as claimed for billing purposes too —
        // billing the buyer only once someone accepts would let a workspace park invites on
        // seats it never pays for.
        var claimed = rows.Count;
        var summary = SeatBilling.Summarize(plan, workspace.Seats, claimed);

        var now = DateTime.UtcNow;
        var periodEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var nextSeatCost = SeatBilling.BillableSeats(plan, workspace.Seats + 1) > summary.BillableSeats
            ? SeatBilling.Prorate(PlanCatalog.SeatMonthlyAmount(plan), 1, now, periodEnd)
            : 0m;

        return new SeatReportDto(
            summary.Plan,
            summary.Seats,
            summary.IncludedSeats,
            summary.BillableSeats,
            summary.ClaimedSeats,
            summary.FreeSeats,
            summary.SeatUnitAmount,
            summary.MonthlySeatAmount,
            nextSeatCost,
            rows.Select(r => new SeatMemberDto(
                r.InviteEmail,
                r.Name,
                r.Role.ToString().ToLowerInvariant(),
                r.JoinedAt is null ? StatusPending : StatusActive,
                r.JoinedAt)).ToList());
    }
}

/// <summary>
/// GET /api/workspace/seats/export — the seat roster finance asks for at renewal (issue #278).
/// Admins only: this is the list of who the company is paying for, which is a different
/// question from who is in the workspace.
/// </summary>
public sealed class ExportSeatReportEndpoint : EndpointWithoutRequest
{
    private const string CsvHeader = "email,name,role,status,joined_utc,seat_unit_amount,currency";

    public override void Configure()
    {
        Get("/api/workspace/seats/export");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Export the seat roster as CSV";
            s.Description = "Workspace admins only. One row per claimed seat, with the per-seat price for the period.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(
            Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var report = await GetSeatReportEndpoint.BuildAsync(db, seat, ct);

        var csv = new StringBuilder();
        csv.AppendLine(CsvHeader);

        foreach (var member in report.Members)
        {
            csv.Append(Csv(member.Email));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(member.Name));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(member.Role));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(member.Status));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(member.JoinedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(report.SeatUnitAmount.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(PricingKeys.DefaultCurrency));
            csv.AppendLine();
        }

        var filename = $"quickshare-seats-{DateTime.UtcNow:yyyyMMdd}.csv";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        await Send.StringAsync(csv.ToString(), StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
