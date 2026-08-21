# Enterprise trust

Issues `#190` (tenant isolation), `#191` (SIEM streaming), `#184` (self-serve DPA),
`#147` / `#323` (SOC 2 readiness and roadmap). One subsystem because they answer one question:
what a buyer's vendor review asks for before the deal closes.

## Tenant isolation (`#190`)

### The mechanism: an explicit predicate, not a global filter

Every workspace-scoped query carries its own `WorkspaceId` (or `UserId`) `Where` clause, at the
call site. That is a decision, not an omission.

An EF Core global query filter is the tempting alternative — one line, applies everywhere. It is
also lost silently by `IgnoreQueryFilters`, by raw SQL, by a projection built before the filter
applies, and by any `DbSet` reached through a navigation the filter does not cover. The failure
mode is org A reading org B's files with no exception anywhere and nothing in the logs.

An explicit predicate is worse ergonomics and better safety: it can be read, grepped, reviewed
in a diff, and — crucially — *tested*. `TenantIsolationTests` is what actually holds the line.

### The test suite is the control

Seven cases, each probing with a **real, valid id belonging to another tenant**. A random GUID
would pass even with scoping removed entirely, which would make the suite worse than useless.

Covered surfaces: folder list / prefill / rename / delete, workspace read, member revoke and
role change, ownership transfer, workspace audit, workspace report, delivery list and mutation,
invoice read and list, streaming config.

**When you add a workspace-scoped surface, add a case here.** A route that takes an id and is
not in that file is a route nobody has proven is scoped.

### Observing the probe

`TenantGuard` records a `CrossTenantAccessBlocked` event when a caller reaches for a resource
that exists but belongs to somebody else. Two properties matter:

- **The response is unchanged.** A cross-tenant miss and a nonexistent id return the same 404.
  Anything else would turn the error code into an existence oracle.
- **A miss on an id that belongs to nobody is not recorded.** Otherwise the signal is drowned in
  the noise of ordinary 404s, and the whole point is that a *burst* of these means something.

It costs one extra query, on a path that has already failed.

## SIEM streaming (`#191`)

Enterprise SOC teams do not log into somebody else's UI. "We have an audit log" is not what
procurement verifies; "your audit log lands in our Splunk" is.

### No outbox, on purpose

`security_events` is already durable and append-only — which is precisely what an outbox is
*for*. So the sink stores a **cursor** into it rather than a copy of it:

```
read forward from cursor → POST batch → advance cursor on success
```

A failed batch does not advance, so the next run re-sends it. Delivery is at-least-once;
duplicates on retry are the deliberate trade, because a SIEM de-duplicates on `externalId`
whereas a dropped audit event is unrecoverable and is the one thing the feature exists to
prevent. An outbox table would be a second copy of the same rows with its own chance to drift
out of step with the first.

The cursor is `(CreatedAt, Id)`, not `CreatedAt` alone. Two events written in the same
millisecond would otherwise order arbitrarily, and one of them could be skipped forever.

### Formats

One schema — who, what, when, from where, which tenant — in three envelopes:

| Format | Wire shape | For |
|---|---|---|
| `json` | `{"events":[…]}` | generic webhooks, Elastic |
| `cef` | CEF:0 lines, newline separated | ArcSight, QRadar, Splunk |
| `splunk_hec` | newline-delimited `{"event":…}`, token in header | Splunk HEC |

The fields are identical in all three. A customer who migrates from Splunk to Elastic should not
discover that half their saved searches keyed on a field we only emit in one format.

CEF escaping is not cosmetic: an unescaped `=` inside an extension value silently swallows the
following field, and a raw newline ends the event early. Both are tested.

**The IP ships hashed**, exactly as stored. A SIEM export is not a reason to start keeping raw
addresses we deliberately never kept; a receiving SOC still correlates on the hash, which is
stable per address.

### Signing and failure

Non-HEC batches carry `X-QuickShare-Signature`: HMAC-SHA256 over `{timestamp}.{body}`. The
timestamp is *inside* the signed payload so a captured batch cannot be replayed later as fresh.
HEC authenticates with its own token scheme, so signing on top would be noise.

HTTPS only. A sink that fails 10 times running is **parked**: hammering a dead endpoint every
five minutes helps nobody, and a parked sink with its last error visible is something an admin
can act on. Saving the sink clears the failure count — fixing the endpoint and pressing save is
the obvious gesture, and a separate "resume" button would be ceremony.

A new sink starts its cursor at **now**. Back-filling months of history into somebody's SIEM on
first save is a bill they did not agree to.

The secret is write-only: the API reports whether one is set, never what it is. Blank on save
leaves the stored value alone, because the field is never populated back into the form.

## Self-serve DPA (`#184`)

GDPR Article 28 requires a controller–processor contract, and enterprise procurement blocks the
purchase without one. Signing online turns a two-week legal round trip into two clicks.

**The row is the artefact.** There is no separately generated PDF to keep in sync — the page
renders the stored signature beside the published terms, so the evidence and the document
cannot disagree. Printing produces the PDF.

- **Versioned.** A signature records which version was signed. `NeedsResign` is the field
  procurement cares about: a signature against older terms is still a real agreement, just not
  agreement to what we publish today, and pretending otherwise surfaces during a vendor review
  rather than before one.
- **Signing the same version twice is refused.** A second row would make "when did they agree"
  ambiguous, which is the one question the record exists to answer.
- **The terms page is public.** A DPA behind a login is a DPA their legal team cannot review.
  Only the signing panel needs an account.
- The signer email defaults to the account email — the account is already the verified identity.
- The IP is hashed, and signing lands on the account's security timeline.

## SOC 2 readiness (`#147`, `#323`)

`/soc2` publishes the control map. The first line says **not certified, no audit, no report**,
because a readiness page that reads like a certification is what loses the deal in the vendor
review it was written to win.

Status is three-valued. "Partial" is the honest answer for a control that exists but has no
independent evidence behind it yet, and collapsing that into "implemented" is exactly the
overclaim an auditor finds. Ten criteria are mapped (CC6.1/6.2/6.6/6.7, CC7.2/7.3, CC8.1, A1.2,
C1.1, P4.2), followed by a named gap list and a four-phase roadmap.

The gap list is deliberately specific — no pen test on record, no written policy set, no
scheduled restore drill with retained evidence, no vendor-risk process, no auditor engaged.
These are open issues, not oversights being quietly hoped past.

**Keep the page true.** When one of the gaps closes, move the row; when a control regresses,
move it back. A trust page that drifts is worse than none, because it is a claim somebody will
check.

## Extending this

- **New workspace-scoped route that takes an id?** Add a case to `TenantIsolationTests` probing
  it with another tenant's real id. Not optional — that suite *is* the isolation control.
- **New audit event type?** It streams automatically; the sink reads the whole event table for
  its workspace. Just make sure it is written with a `WorkspaceId`, or it will never ship.
- **New SIEM format?** Add it to `SiemFormat.All` and `SiemEventFormatter`. Keep the field set
  identical to the others.
- **Changing the DPA wording?** Bump `DpaTerms.CurrentVersion` and its date. Never edit terms in
  place — existing signatures point at a version that must keep meaning what it meant.
- **Closing a SOC 2 gap?** Update `/soc2` in the same change, both locales.

## Tests

`tests/QuickShare.Server.Tests/E2E/TenantIsolationTests.cs` (7) — folders, workspace management,
audit, report, deliveries, invoices and streaming config all probed with a real id from another
tenant; the probe recorded server-side while the response stays indistinguishable from a plain
404.

`tests/QuickShare.Server.Tests/E2E/SiemAndDpaTests.cs` (16) — plaintext and unknown-format
refusal, cursor starting at now, write-only secret, parked-sink reset, admin gating, CEF
delimiter escaping, Splunk newline-delimited shape, JSON envelope, HMAC construction, filter
parsing; DPA unsigned default, signing evidence and audit, required fields, double-sign refusal,
and the older-version re-sign prompt.
