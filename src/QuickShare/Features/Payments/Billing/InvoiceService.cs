using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Payments.Billing;

/// <summary>
/// The one writer for the billing ledger (issues #278 seats, #141 overage, #112 e-invoice,
/// #192 PO/net terms).
///
/// The model is a **draft invoice per period**. Mid-cycle charges — a seat added on the 12th,
/// a gigabyte of overage at 3am — append or update a line on that draft as they happen. The
/// alternative is reconstructing them from history at billing time, which means every future
/// change to seats or usage has to remember to leave enough evidence behind for a query nobody
/// runs until the end of the month. Recording the charge where it is incurred is both smaller
/// and harder to get wrong.
///
/// Issuing freezes the draft: it takes a number, a due date, and a *copy* of the buyer's
/// company details. An invoice has to keep saying what it said when it was issued, even after
/// the buyer moves office or changes their tax code.
/// </summary>
public sealed class InvoiceService(AppDbContext db)
{
    private const int BytesPerGb = 1024 * 1024 * 1024;
    /// <summary>Vietnamese VAT on SaaS. One rate, because the product has one.</summary>
    private const decimal VatRate = 0.10m;

    /// <summary>
    /// The open ledger for the caller's current billing month, created on first use. Callers
    /// mutate the returned invoice and save; this method never saves on its own, so a charge
    /// and whatever produced it land in one transaction.
    /// </summary>
    public async Task<Invoice> GetOrCreateDraftAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Look in the change tracker before the database. A caller that adds a charge and then
        // issues in the same unit of work has a draft that exists but has not been saved yet,
        // and a query would miss it — producing a second draft, and an issued invoice with none
        // of the lines that were just recorded on the first one.
        var pending = db.Invoices.Local.FirstOrDefault(i => i.UserId == userId
            && i.Status == InvoiceStatus.Draft
            && i.PeriodStart == periodStart);

        if (pending is not null)
            return pending;

        var draft = await db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Status == InvoiceStatus.Draft
                && i.PeriodStart == periodStart, ct);

        if (draft is not null)
            return draft;

        draft = new Invoice
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = InvoiceStatus.Draft,
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddMonths(1),
            CreatedAt = now,
        };
        db.Invoices.Add(draft);
        return draft;
    }

    /// <summary>
    /// Adds a charge to the open draft. Seat lines accumulate (two additions in a month are two
    /// separate prorated events); overage is a single running line that is rewritten as usage
    /// grows, because a member uploading all afternoon should produce one charge, not two
    /// hundred.
    /// </summary>
    public async Task AddOrUpdateLineAsync(
        Guid userId,
        string kind,
        string description,
        decimal quantity,
        decimal unitAmount,
        CancellationToken ct)
    {
        var draft = await GetOrCreateDraftAsync(userId, ct);
        var amount = decimal.Round(quantity * unitAmount, 0, MidpointRounding.AwayFromZero);

        if (kind == InvoiceLineKind.Overage)
        {
            var existing = draft.Lines.FirstOrDefault(l => l.Kind == InvoiceLineKind.Overage);
            if (existing is not null)
            {
                existing.Description = description;
                existing.Quantity = quantity;
                existing.UnitAmount = unitAmount;
                existing.Amount = amount;
                Retotal(draft);
                return;
            }
        }

        draft.Lines.Add(new InvoiceLine
        {
            Id = Guid.CreateVersion7(),
            InvoiceId = draft.Id,
            Kind = kind,
            Description = Truncate(description),
            Quantity = quantity,
            UnitAmount = unitAmount,
            Amount = amount,
            CreatedAt = DateTime.UtcNow,
        });

        Retotal(draft);
    }

    /// <summary>
    /// Freezes the draft into a real invoice (issues #112, #192). Card payments arrive already
    /// settled; a buyer with approved net terms gets a due date instead, and the overdue sweep
    /// takes it from there.
    ///
    /// Returns null when there is nothing to bill — an empty draft is not an invoice, and
    /// issuing zero-total paperwork is how a buyer's accounting team learns to ignore us.
    /// </summary>
    public async Task<Invoice?> IssueAsync(Guid userId, Guid? paymentEventId, bool alreadyPaid, CancellationToken ct)
    {
        var draft = await GetOrCreateDraftAsync(userId, ct);
        if (draft.Lines.Count == 0)
            return null;

        var profile = await db.BillingProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var now = DateTime.UtcNow;
        var terms = profile?.EffectiveNetTermsDays ?? 0;

        draft.Status = alreadyPaid ? InvoiceStatus.Paid : InvoiceStatus.Issued;
        draft.Number = await NextNumberAsync(now, ct);
        draft.IssuedAt = now;
        draft.DueAt = now.AddDays(terms);
        draft.PaidAt = alreadyPaid ? now : null;
        draft.PaymentEventId = paymentEventId;
        draft.NetTermsDays = terms;
        draft.CompanyName = profile?.CompanyName ?? string.Empty;
        draft.TaxCode = profile?.TaxCode ?? string.Empty;
        draft.Address = profile?.Address ?? string.Empty;
        draft.PoNumber = profile?.PoNumber ?? string.Empty;
        draft.UpdatedAt = now;

        Retotal(draft);
        return draft;
    }

    /// <summary>
    /// Sequential per calendar year, zero-padded: <c>QS-2026-000123</c>. Derived from the count
    /// of numbered invoices in the year rather than a database sequence, which keeps the scheme
    /// portable; the unique index on <c>Number</c> is what actually guarantees no collision, so
    /// a concurrent issue fails loudly instead of silently duplicating a legal identifier.
    /// </summary>
    private async Task<string> NextNumberAsync(DateTime now, CancellationToken ct)
    {
        var yearStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var issued = await db.Invoices
            .CountAsync(i => i.Number != string.Empty && i.IssuedAt >= yearStart, ct);

        return $"QS-{now.Year}-{issued + 1:D6}";
    }

    /// <summary>Bytes past the pool, priced per GB-month and rounded to two decimals of a GB.</summary>
    public static decimal OverageGb(long overageBytes) =>
        decimal.Round(overageBytes / (decimal)BytesPerGb, 2, MidpointRounding.AwayFromZero);

    private static void Retotal(Invoice invoice)
    {
        invoice.Subtotal = invoice.Lines.Sum(l => l.Amount);
        invoice.TaxAmount = decimal.Round(invoice.Subtotal * VatRate, 0, MidpointRounding.AwayFromZero);
        invoice.Total = invoice.Subtotal + invoice.TaxAmount;
    }

    private static string Truncate(string value) =>
        value.Length > InvoiceLine.DescriptionMaxLength
            ? value[..InvoiceLine.DescriptionMaxLength]
            : value;
}

/// <summary>
/// Seat arithmetic (issue #278). Pure: the same numbers have to appear in the seat meter, the
/// finance CSV and the invoice line, and three implementations would eventually disagree in
/// front of somebody's accountant.
/// </summary>
public static class SeatBilling
{
    /// <summary>Seats charged for: everything past what the plan already includes.</summary>
    public static int BillableSeats(string plan, int seats) =>
        Math.Max(0, seats - PlanCatalog.IncludedSeats(plan));

    /// <summary>
    /// The charge for adding seats partway through a period. Whole periods are never prorated
    /// down to zero: a seat bought on the last day of the month still costs one day, and
    /// rounding that away would make the last day of every month free.
    /// </summary>
    public static decimal Prorate(decimal monthlyAmount, int seats, DateTime now, DateTime periodEnd)
    {
        if (seats <= 0 || monthlyAmount <= 0)
            return 0m;

        var periodStart = new DateTime(periodEnd.Year, periodEnd.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var totalDays = Math.Max(1, (periodEnd - periodStart).TotalDays);
        var remainingDays = Math.Clamp((periodEnd - now).TotalDays, 1, totalDays);

        var fraction = (decimal)(remainingDays / totalDays);
        return decimal.Round(monthlyAmount * seats * fraction, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The seat picture an admin and a finance team both read (issue #278): what the plan
    /// includes, what is bought, what is actually claimed, and what the next bill will carry.
    /// </summary>
    public static SeatSummary Summarize(string plan, int seats, int claimed)
    {
        var included = PlanCatalog.IncludedSeats(plan);
        var billable = BillableSeats(plan, seats);
        var unit = PlanCatalog.SeatMonthlyAmount(plan);

        return new SeatSummary(
            plan,
            seats,
            included,
            billable,
            claimed,
            Math.Max(0, seats - claimed),
            unit,
            billable * unit);
    }
}

public sealed record SeatSummary(
    string Plan,
    int Seats,
    int IncludedSeats,
    int BillableSeats,
    int ClaimedSeats,
    int FreeSeats,
    decimal SeatUnitAmount,
    decimal MonthlySeatAmount);

/// <summary>
/// Resolves whose bill a workspace member's charges land on (issues #278, #141). The owner
/// pays — the same rule <see cref="WorkspaceService"/> already applies to the plan and
/// <c>QuotaService</c> to the quota pool, kept in one place so billing cannot drift from the
/// tier gates it is supposed to be charging for.
/// </summary>
public sealed class BillingAccountResolver(AppDbContext db)
{
    public async Task<Guid> ResolvePayerAsync(Guid userId, CancellationToken ct)
    {
        var ownerId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.Workspace.OwnerId)
            .FirstOrDefaultAsync(ct);

        return ownerId ?? userId;
    }
}
