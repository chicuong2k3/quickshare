using System.Text;
using Microsoft.Extensions.Options;
using QuickShare.Extensions;
using QuickShare.Features.Deliveries.LegalHold;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Payments.Billing;

public sealed record InvoiceLineDto(string Kind, string Description, decimal Quantity, decimal UnitAmount, decimal Amount);

public sealed record InvoiceDto(
    Guid Id,
    string Number,
    string Status,
    string Currency,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime? IssuedAt,
    DateTime? DueAt,
    DateTime? PaidAt,
    string CompanyName,
    string TaxCode,
    string Address,
    string PoNumber,
    int NetTermsDays,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    bool IsOverdue,
    IReadOnlyList<InvoiceLineDto> Lines);

/// <summary>
/// Shared projection so the list, the detail view and the printable page all describe an
/// invoice the same way. Overdue is computed rather than trusted from the row: the sweep runs
/// hourly, and a bill that passed its due date twenty minutes ago is already late.
/// </summary>
internal static class InvoiceMapper
{
    public static InvoiceDto Map(Invoice invoice) => new(
        invoice.Id,
        invoice.Number,
        invoice.Status,
        invoice.Currency,
        invoice.PeriodStart,
        invoice.PeriodEnd,
        invoice.IssuedAt,
        invoice.DueAt,
        invoice.PaidAt,
        invoice.CompanyName,
        invoice.TaxCode,
        invoice.Address,
        invoice.PoNumber,
        invoice.NetTermsDays,
        invoice.Subtotal,
        invoice.TaxAmount,
        invoice.Total,
        invoice.PaidAt is null && invoice.DueAt is { } due && due < DateTime.UtcNow && !invoice.IsDraft,
        invoice.Lines
            .OrderBy(l => l.CreatedAt)
            .Select(l => new InvoiceLineDto(l.Kind, l.Description, l.Quantity, l.UnitAmount, l.Amount))
            .ToList());
}

/// <summary>
/// GET /api/billing/invoices — the caller's bills (issue #112).
///
/// The open draft is included. A buyer watching seat and overage charges accumulate mid-month
/// is the whole point of recording them when they happen; hiding the draft until issue day
/// would turn every invoice into a surprise.
/// </summary>
public sealed class ListInvoicesEndpoint : EndpointWithoutRequest<IReadOnlyList<InvoiceDto>>
{
    public override void Configure()
    {
        Get("/api/billing/invoices");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "List the caller's invoices";
            s.Description = "Newest first, including the current period's open draft so seat and overage charges are visible before they are billed.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var invoices = await db.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.UserId == user.Id && i.Status != InvoiceStatus.Void)
            .OrderByDescending(i => i.PeriodStart)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        await Send.OkAsync(invoices.Select(InvoiceMapper.Map).ToList(), ct);
    }
}

/// <summary>GET /api/billing/invoices/{Id} — one invoice with its lines (issue #112).</summary>
public sealed class GetInvoiceEndpoint : EndpointWithoutRequest<InvoiceDto>
{
    public override void Configure()
    {
        Get("/api/billing/invoices/{Id}");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s => s.Summary = "Get one invoice");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == Route<Guid>("Id") && i.UserId == user.Id, ct);

        if (invoice is null)
        {
            ThrowError(ErrorCodes.InvoiceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await Send.OkAsync(InvoiceMapper.Map(invoice), ct);
    }
}

/// <summary>
/// GET /api/billing/invoices/export — every issued invoice as CSV, for the buyer's accounting
/// (issues #112, #192). Drafts are excluded: an unissued ledger is not a document anyone should
/// be filing.
/// </summary>
public sealed class ExportInvoicesEndpoint : EndpointWithoutRequest
{
    private const string CsvHeader = "number,status,issued_utc,due_utc,paid_utc,po_number,tax_code,company,subtotal,tax,total,currency";

    public override void Configure()
    {
        Get("/api/billing/invoices/export");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Export issued invoices as CSV";
            s.Description = "One row per issued invoice, for the buyer's finance team. Drafts are excluded.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var invoices = await db.Invoices
            .AsNoTracking()
            .Where(i => i.UserId == user.Id && i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Void)
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine(CsvHeader);

        foreach (var invoice in invoices)
        {
            csv.Append(Csv(invoice.Number));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(invoice.Status));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(invoice.IssuedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(invoice.DueAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(invoice.PaidAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(invoice.PoNumber));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(invoice.TaxCode));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(invoice.CompanyName));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(invoice.Subtotal.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(invoice.TaxAmount.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(invoice.Total.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(invoice.Currency));
            csv.AppendLine();
        }

        var filename = $"quickshare-invoices-{DateTime.UtcNow:yyyyMMdd}.csv";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        await Send.StringAsync(csv.ToString(), StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
    }

    /// <summary>Company names and PO references are free text — always quote, double embedded quotes.</summary>
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>
/// POST /api/admin/billing/invoices/{Id}/paid — record a bank transfer against a net-terms
/// invoice (issue #192).
///
/// Platform admins only, and manual by design: a wire lands in a bank account this application
/// cannot see, so someone has to reconcile it. Marking an already-paid invoice again is a
/// no-op rather than an error — remittance emails get forwarded twice.
/// </summary>
public sealed class MarkInvoicePaidEndpoint : EndpointWithoutRequest<InvoiceDto>
{
    public override void Configure()
    {
        Post("/api/admin/billing/invoices/{Id}/paid");
        Description(b => b.WithTags(ApiTags.Admin));
        Summary(s =>
        {
            s.Summary = "Mark a net-terms invoice as paid";
            s.Description = "Platform admins only. Reconciles a bank transfer this application cannot observe.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();

        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == Route<Guid>("Id"), ct);

        if (invoice is null)
        {
            ThrowError(ErrorCodes.InvoiceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (invoice.IsDraft)
        {
            ThrowError(ErrorCodes.InvoiceNotIssued, StatusCodes.Status409Conflict);
            return;
        }

        if (invoice.PaidAt is null)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = DateTime.UtcNow;
            invoice.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(InvoiceMapper.Map(invoice), ct);
    }
}
