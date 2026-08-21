namespace QuickShare.Data.Entities;

/// <summary>
/// The buyer's billing identity (issues #112, #192). One per account, and the account that
/// matters is the workspace owner — they are the bill payer, so their profile is what every
/// invoice for that workspace is cut against.
///
/// Net terms are stored but inert until a platform admin approves them: "pay us in 60 days"
/// is a credit decision, and self-service credit is how a SaaS discovers bad debt.
/// </summary>
public record BillingProfile : BaseAuditEntity
{
    public const int CompanyNameMaxLength = 200;
    public const int TaxCodeMaxLength = 20;
    public const int AddressMaxLength = 400;
    public const int PoNumberMaxLength = 60;
    /// <summary>Terms a buyer may ask for. Anything else is rejected rather than rounded.</summary>
    public static readonly int[] AllowedNetTermsDays = [0, 30, 60];

    public Guid UserId { get; set; }
    /// <summary>Registered company name; blank means an individual buyer and no VAT invoice.</summary>
    public string CompanyName { get; set; } = string.Empty;
    /// <summary>Vietnamese tax code (mã số thuế). Digits and one optional branch suffix.</summary>
    public string TaxCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string BillingEmail { get; set; } = string.Empty;
    /// <summary>Purchase-order reference the buyer's procurement requires on every invoice (#192).</summary>
    public string PoNumber { get; set; } = string.Empty;
    /// <summary>Requested payment terms. Only binding once <see cref="NetTermsApprovedAt"/> is set.</summary>
    public int NetTermsDays { get; set; }
    public DateTime? NetTermsApprovedAt { get; set; }
    public Guid? NetTermsApprovedById { get; set; }

    public User User { get; init; } = null!;

    /// <summary>Terms actually granted. An unapproved request bills on issue, like a card.</summary>
    public int EffectiveNetTermsDays => NetTermsApprovedAt is null ? 0 : NetTermsDays;
    /// <summary>A VAT invoice needs a named company and a tax code; without both it is a receipt.</summary>
    public bool IsBusiness => !string.IsNullOrWhiteSpace(CompanyName) && !string.IsNullOrWhiteSpace(TaxCode);
}

public static class InvoiceStatus
{
    /// <summary>Open ledger for the current period — seat and overage lines accumulate here.</summary>
    public const string Draft = "draft";
    /// <summary>Numbered and sent. Waiting on payment (net terms) or already settled by card.</summary>
    public const string Issued = "issued";
    public const string Paid = "paid";
    public const string Overdue = "overdue";
    public const string Void = "void";
}

public static class InvoiceLineKind
{
    public const string Plan = "plan";
    public const string Seat = "seat";
    public const string Overage = "overage";
}

/// <summary>
/// One bill (issues #112 e-invoice, #192 PO/net terms, and the destination for #278 seat and
/// #141 overage charges).
///
/// A draft invoice is the period's open ledger: mid-cycle seat additions and metered overage
/// append lines to it as they happen, so the charge is recorded at the moment it is incurred
/// rather than reconstructed from history at billing time. Issuing freezes it — the buyer's
/// company details are copied onto the row, because an invoice has to keep saying what it said
/// when it was issued even after the buyer moves office.
/// </summary>
public record Invoice : BaseAuditEntity
{
    public const int NumberMaxLength = 32;
    public const int StatusMaxLength = 16;

    public Guid UserId { get; set; }
    /// <summary>Sequential, per year. Empty while the invoice is still a draft.</summary>
    public string Number { get; set; } = string.Empty;
    public string Status { get; set; } = InvoiceStatus.Draft;
    public string Currency { get; set; } = PricingKeys.DefaultCurrency;

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? PaidAt { get; set; }

    // Frozen copies of the billing profile, taken at issue time.
    public string CompanyName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PoNumber { get; set; } = string.Empty;
    public int NetTermsDays { get; set; }

    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    /// <summary>The PayOS payment this invoice was issued against; null for a net-terms bill.</summary>
    public Guid? PaymentEventId { get; set; }

    public User User { get; init; } = null!;
    public List<InvoiceLine> Lines { get; init; } = [];

    public bool IsDraft => Status == InvoiceStatus.Draft;
}

/// <summary>One charge on an invoice (#278 seats, #141 overage, plan subscription).</summary>
public record InvoiceLine
{
    public const int DescriptionMaxLength = 200;
    public const int KindMaxLength = 16;

    public Guid Id { get; init; }
    public Guid InvoiceId { get; set; }
    public string Kind { get; set; } = InvoiceLineKind.Plan;
    public string Description { get; set; } = string.Empty;
    /// <summary>Seats, or GB of overage. Fractional for prorated and metered lines.</summary>
    public decimal Quantity { get; set; }
    public decimal UnitAmount { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Invoice Invoice { get; init; } = null!;
}
