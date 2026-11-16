# Billing ledger

Issues `#278` (team seats), `#141` (Studio overage), `#112` (VN e-invoice), `#192` (purchase
orders + net terms). One subsystem: they all write to, or read from, the same ledger, and three
of them are meaningless without an invoice to land on.

Checkout, PayOS and subscription lifecycle live in
[`payment-architecture.md`](payment-architecture.md). This document covers what happens *after*
a plan is active: what a workspace accrues during a period, and what document comes out the
other end.

## The model: a draft invoice per period

```
seat added (#278) ─┐
overage metered (#141) ─┼─→ draft invoice for the month ──(payment or terms)──→ issued
plan payment (#112) ─┘
```

Mid-cycle charges append to an open draft **as they happen**. The alternative — reconstructing
them at billing time from seat history and usage rows — means every future change to seats or
metering has to remember to leave enough evidence behind for a query nobody runs until the end
of the month. Recording the charge where it is incurred is both a smaller diff and harder to
get wrong.

Issuing **freezes** the draft: it takes a number, a due date, and a *copy* of the buyer's
company name, tax code, address and PO reference. An invoice has to keep saying what it said
when it was issued, even after the buyer moves office or re-registers their tax code.

**An empty draft is never issued.** Zero-total paperwork is how a buyer's accounting team
learns to ignore us.

### One trap worth knowing about

`GetOrCreateDraftAsync` checks `db.Invoices.Local` **before** querying the database. A caller
that adds a charge and then issues inside the same unit of work has a draft that exists but has
not been saved; a query would miss it, create a second draft, and issue an invoice with none of
the lines just recorded. This was a real bug caught by the tests, not a hypothetical.

## Data model

| Table | Row means |
|---|---|
| `billing_profiles` | one buyer identity: company, tax code, address, PO reference, requested net terms + approval |
| `invoices` | one bill: number, status, period, frozen buyer details, subtotal/VAT/total |
| `invoice_lines` | one charge: kind (`plan`/`seat`/`overage`), quantity, unit amount, amount |

Plus two columns on `user_usage`: `OverageBytes` and `AlertedThresholdPercent`.

Invoice numbers are `QS-{year}-{000001}`, sequential per calendar year. The count-based scheme
keeps things portable; the **unique index on `Number`** is what actually guarantees no
collision, so a concurrent issue fails loudly instead of silently duplicating a legal
identifier.

## Who pays

The workspace **owner**. `BillingAccountResolver` applies the same rule
`WorkspaceService` already uses for the plan and `QuotaService` for the quota pool, kept in one
place so billing cannot drift from the tier gates it is charging for.

## Seats (`#278`)

`PlanCatalog.IncludedSeats` — Free 1, Pro 3, Studio 10. `SeatBilling.BillableSeats` is
everything past that. Prices live in `PlanCatalog` beside the plan prices, deliberately *not*
in `PlanTierLimits`: those are usage caps read on every upload, and a hot path should not
depend on a number only accounting cares about.

`SeatBilling` is pure. The same figures appear in the seat meter, the finance CSV and the
invoice line, and three implementations would eventually disagree in front of somebody's
accountant.

- **Growth is charged, prorated to the end of the period.** A seat bought on the last day of a
  month still costs one day — rounding that to zero would make the last day of every month
  free.
- **Shrinking is not credited.** It frees seats for the next period. This codebase has no
  refund flow, and faking one is worse than not having it.
- **A pending invite counts as a claimed seat.** Billing only on acceptance would let a
  workspace park invites on seats it never pays for.
- Adding a seat does **not** interrupt with a checkout. The buyer already has a subscription;
  making "add a colleague" a payment flow is how a team ends up sharing one login.

`GET /api/workspace/seats` is readable by any seat holder — seat pricing is not a secret from
the people occupying the seats, and "3 of 3 claimed" answers a refused invite without a support
ticket. `GET /api/workspace/seats/export` is admin-only: who the company *pays for* is a
different question from who is in the workspace.

## Overage (`#141`)

**Studio meters; Free and Pro still hard-block.** A hard stop mid-delivery is the wrong failure
for a team on a deadline — but an unbounded bill is a worse surprise than a refused upload when
nobody agreed to pay for overflow.

The switch is `QuotaCheckResult.Metered`, returned instead of `Exceeded` for plans where
`PlanCatalog.AllowsOverage`. Every existing call site tests `IsExceeded`, so Studio stopped
blocking with **no call-site changes** — the same root-cause shape as the quota pooling that
lives in the same service.

`RecordAsync` then tallies `OverageBytes` and rewrites a single running overage line on the
draft. Rewritten, not appended: an afternoon of uploading should produce one charge, not two
hundred line items.

Thresholds at 80/100/120% raise one `Notification` each. `AlertedThresholdPercent` on the usage
row is what makes it *once* — an alert that repeats on every upload past the line is an alert
people mute.

## E-invoice (`#112`)

A billing profile with a company name **and** a valid tax code turns receipts into VAT
invoices; without both it stays a receipt. Vietnamese tax codes are validated
(`^\d{10}(-\d{3})?$`) because the value is copied onto a document the buyer files with their
tax authority — a typo here is a typo on their filing.

VAT is a flat 10%, computed at issue time and stored, not recomputed on read.

The PayOS webhook adds the plan line and issues, guarded by the **same idempotency check** as
the payment event: PayOS retries webhooks, and two invoices for one payment is a filing error,
not a cosmetic one.

**Not shipped: a certified Vietnamese e-invoice.** Decree 123/2020 requires issuance through a
licensed provider (Viettel HoaDon, MISA, …). What ships is a print-ready invoice page — the
browser makes the PDF — carrying the correct VAT fields, plus a CSV export for the buyer's
accounting. The page says so in as many words, in both locales. Wiring a licensed provider is
its own integration with its own credentials and its own failure modes.

## Purchase orders and net terms (`#192`)

The PO reference lives on the billing profile and is copied onto every invoice at issue.

Net terms are the interesting part:

- A buyer may **request** 0/30/60 days. Anything else is rejected rather than rounded.
- The request is **inert until a platform admin approves it**. Self-service credit is how a
  SaaS discovers bad debt.
- `EffectiveNetTermsDays` returns 0 unless approved, so an unapproved request bills on issue
  exactly like a card.
- **Changing the requested terms clears a prior approval.** "Approved for 30 days" is not
  approval for 60.

`InvoiceOverdueJob` (hourly) flips issued → overdue past the due date, raises a notification and
sends one email. Only terms invoices can be late: a card payment settles the moment it is
issued. The status filter is what makes the transition happen **once** — otherwise the buyer
gets the same dunning email every hour until they pay.

`POST /api/admin/billing/invoices/{id}/paid` reconciles a bank transfer. Manual by design: the
wire lands in an account this application cannot see. Marking an already-paid invoice again is
a no-op, because remittance emails get forwarded twice.

## Extending this

- **New recurring charge?** Add an `InvoiceLineKind` and write it through
  `InvoiceService.AddOrUpdateLineAsync`. Do not insert `InvoiceLine` rows directly — the
  retotal and the draft-tracking rule live in that method.
- **New code reading a quota result?** `IsExceeded` means blocked; `IsMetered` means allowed
  and billable. Treating `IsMetered` as a block silently reinstates the hard stop for Studio.
- **New plan tier?** It needs `IncludedSeats`, `SeatMonthlyAmount`, `OverageGbAmount` and
  `AllowsOverage` entries — the switches fall through to the Pro-shaped defaults otherwise.
- **Never mutate an issued invoice's totals or buyer details.** Issue a new one. The frozen copy
  is the point.

## Tests

`tests/QuickShare.Server.Tests/E2E/BillingLedgerTests.cs` (18) — seat arithmetic and proration
as pure functions; growth charged once and shrink not credited; pending invites counted;
roster export admin-gated; Studio metered past quota while Pro still blocks; overage as one
running line; threshold alert once per level; issue freezes buyer details and numbers the
invoice; empty period not billed; draft visible in the list but excluded from export; net terms
inert until approved, cleared on change, due-dated and swept to overdue exactly once; malformed
tax code and out-of-set terms refused; admin endpoints refused to a non-admin.

Note for future test authors: `CreateAuthenticatedClient` hands out the **configured admin
email**, so admin-gate assertions written with it are vacuous. `NonAdminClientAsync` in that
file builds a client with a different email.
