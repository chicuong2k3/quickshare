# QuickShare — Compliance Architecture (issues #266, #161, #142/#166)

## Overview

Every delivery already has per-delivery defaults: an expiry the sender picks, a download
limit, an optional password/OTP, and a soft-delete/purge lifecycle. This doc covers the
four controls a legal or compliance team layers **on top of** those defaults, because a
sender's own choices are not enough when a regulator, a court, or a customer contract is
involved:

| Issue | Control | Overrides |
|-------|---------|-----------|
| #266 | **Legal hold** — freeze a delivery against every deletion path | the whole deletion lifecycle, including right-to-erasure |
| #161 | **Workspace retention policy** — account-wide ceiling on share lifetime | the sender's expiry choice (downward only) |
| #142 | **Delivery IP allowlist** — CIDR scope on who may receive | "anyone with the link" |
| #166 | **Country rules** — allow/deny by edge-resolved country | "anyone with the link" |

They ship as one subsystem because they share one shape: a rule an operator sets that the
rest of the app must obey without being asked twice. Two of them (#266, #161) constrain
**lifetime**; two of them (#142, #166) constrain **access**. All four ride on the same
migration and the same audit/analytics plumbing.

File paths below are relative to `src/QuickShare/`.

---

## 1. Invariants

- **A hold is absolute.** While `LegalHoldAt` is set, nothing deletes the row or its R2
  objects — not the owner, not a purge, not a sweep, not account deletion.
- **A retention policy is a ceiling, never a floor.** It clamps an expiry down; it never
  extends one, and a shorter sender choice always survives.
- **Clamping is not a validation error.** The bytes already landed; refusing the commit
  would lose an upload over a setting the sender cannot see.
- **Scope rules fail closed.** A rule that exists refuses anyone it does not explicitly
  satisfy. A malformed rule means "does not match", never "matches everything".
- **A per-caller verdict never enters a shared cache.** Rule *snapshots* are cached per
  share code; the *decision* is always recomputed against the live request.
- **A refusal leaks nothing.** A restricted recipient learns that a restriction exists and
  nothing about the file.

---

## 2. Legal Hold (issue #266)

### 2.1 Primitive

Three columns on `deliveries` (`Data/Entities/Delivery.cs`):

| Field | Type | Notes |
|-------|------|-------|
| `LegalHoldAt` | `DateTime?` | Null = not held. **This is the flag every deletion path filters on.** |
| `LegalHoldReason` | `string?` | Case id / reference, max `Delivery.LegalHoldReasonMaxLength = 200` (truncated, not rejected) |
| `LegalHoldById` | `Guid?` | Who placed the active hold |

`AppDbContext` indexes `LegalHoldAt` — every deletion path and the retention sweep filter
on it, so it is a hot predicate rather than a rare one.

### 2.2 Endpoints

`Features/Deliveries/LegalHold/` — `LegalHoldHandlers.cs` (`PlaceLegalHoldCommand`,
`LiftLegalHoldCommand`, and `internal static class LegalHoldAudit` shared by both
transitions) plus `LegalHoldEndpoints.cs`. All three are sender-only; FastEndpoints is
secure-by-default here and none of them carry `AllowAnonymous`.

| Route | Method | Behaviour |
|-------|--------|-----------|
| `/api/deliveries/{Id}/legal-hold` | POST | Body `{ reason }`. Reason is required (`legal_hold_reason_required`, 400). Already held → `legal_hold_active`, 409. Not the owner → 404. |
| `/api/deliveries/{Id}/legal-hold/lift` | POST | Manual release only. Not held → `legal_hold_not_active`, 409. |
| `/api/deliveries/legal-hold/export` | GET | CSV report for counsel. Header `share_code,file_name,file_size_bytes,held_at_utc,case_reference,status`; values quoted, embedded `"` doubled. |

Placing or lifting evicts `download:info:{shareCode}` so the recipient-facing snapshot does
not keep serving pre-transition state.

### 2.3 Design decision — no auto-expiry

There is deliberately **no expiry field on a hold and no sweeper that clears one**. A hold
that lapses on its own is evidence loss during active litigation, which is the failure mode
the feature exists to prevent. The only way out is an owner lifting it, and the UI
double-confirms that action because releasing evidence mid-probe is the destructive
direction — not placing the hold.

### 2.4 Enforcement — every deletion path

**This table is the load-bearing part of this document.** A hold is only worth anything if
*every* path that can destroy a row or its bytes checks it. If you add a new deletion path,
you add a row here and a check there (see section 8).

| Path | File | Behaviour when held |
|------|------|---------------------|
| Trash (soft-delete) | `Features/Deliveries/Trash/TrashEndpoints.cs` | 409 `legal_hold_active`. Restore is still allowed — releasing from trash is not destructive. |
| Purge (right-to-erasure) | `Features/Deliveries/Purge/PurgeDelivery.cs` | 409 — the hold outranks the erasure request. |
| Bulk trash | `Features/Deliveries/Bulk/BulkDeliveries.cs` | Row counted in `skipped`, batch continues — one frozen file must not fail the batch. |
| Account deletion | `Features/Users/AccountDeletion/AccountDeletion.cs` | Whole operation refuses, 409. All-or-nothing: a partial delete would orphan frozen bytes with nobody left able to lift the hold. |
| Expiry job object delete | `Infrastructure/Jobs/DeliveryExpiryJob.ExpireDeliveriesAsync` | Link still expires (`Status = Expired`) but the R2 objects are **not** deleted. Access stops; evidence stays. |
| Trashed-row purge sweep | `Infrastructure/Jobs/DeliveryCleanupService.CleanupTrashedDeliveriesAsync` | Held row never leaves the 24h `TrashRetention` queue. |
| Expired-row retention sweep | `Infrastructure/Jobs/DeliveryCleanupService.CleanupExpiredDeliveriesAsync` | Held row survives the 30-day `ExpiredRowRetention`, bytes and all. |
| Retention policy sweep (#161) | `Infrastructure/Jobs/DeliveryExpiryJob.EnforceRetentionAsync` | Held rows excluded in SQL — the hold outranks the policy. Clamping a held row's expiry would only hand a later purge an earlier date. |

The two sweeps and the retention sweep filter `LegalHoldAt == null` **in SQL**, not in
memory: a held row is never loaded into a delete batch in the first place.

### 2.5 UI

`Components/Pages/AccountDeliveries.razor` — `DeliveryItem.IsUnderLegalHold` (derived from
`LegalHoldAt`) drives an `account-delivery__hold` badge whose text carries the state, with
the case reference in the tooltip. `Legal hold` / `Lift hold` buttons swap on state; lifting
is double-confirmed. A `Legal hold report` link sits beside the CSV export in the page lead.
Badge styles are scoped to `AccountDeliveries.razor` and use Shadcn tokens.

---

## 3. Workspace Retention Policy (issue #161)

### 3.1 Semantics — a ceiling, never a floor

`Infrastructure/RetentionPolicy.cs` is pure, static, and has no DI:

| Member | Meaning |
|--------|---------|
| `MinDays = 1`, `MaxDays = 365` | Accepted window |
| `Presets = [1, 7, 14, 30, 90]` | What the settings UI offers; any in-range value is accepted |
| `IsValid(int?)` | Null is valid — it means "clear the policy" |
| `Clamp(DateTime, int?)` | Returns the expiry unchanged when it already sits inside the window |
| `Clamp(DateTime?, int?)` | Nullable overload: a **null** expiry becomes the ceiling (a never-expiring share cannot outlive a policy) |
| `Ceiling(int?)` | Latest instant a share may live to, or null when unpolicied |
| `Exceeds(DateTime?, int?)` | True when the expiry sits beyond the window — a null expiry always exceeds |

The policy clamps the sender's choice rather than replacing it, so a shorter pick always
wins. **Clamping is not a validation error**: at upload time the bytes are already in R2,
and a compliance window is not something the sender chose wrongly.

### 3.2 Account fields

`User.RetentionDays` (`int?`, null = no policy) and `User.RetentionNoExtend` (`bool`,
regulated mode). Convention-mapped — no `OnModelCreating` config and no index, because the
sweep joins through the already-indexed `deliveries.SenderId`. `UserMapper` ignores both via
`[MapperIgnoreSource]`: retention is a compliance setting on the security card, not profile
identity, and must not leak into the profile response.

### 3.3 Endpoints

`Features/Users/Retention/`

| Route | Method | Behaviour |
|-------|--------|-----------|
| `/api/account/retention` | PUT | `{ retentionDays: int?, noExtend: bool }`. Non-Pro/Studio → `requires_paid_plan`, 403. Out-of-range window → `retention_days_invalid`, 400. `retentionDays: null` clears the policy **and** `noExtend`. |
| `/api/account/retention/preview?days=N` | GET | Dry run: `{ days, affected, earliestExpiry, latestExpiry, samples[<=20] }`. One aggregate round trip plus one `Take(20)` sample. Read-only. Cached 30s per `user:retention:preview:{userId}:{days}`. |

The PUT writes via `ExecuteUpdateAsync`, which bypasses the change tracker, so it explicitly
evicts two caches: `UserCacheInvalidator.EvictAsync` (the 5-minute `User` cache that every
clamp site reads) and every `RetentionPreviewHandler.CacheKey(userId, preset)` entry the
settings card could have produced. A stale `User` cache would keep clamping to the previous
window.

### 3.4 Clamp sites — plan cap first, then policy

The order is always **plan cap, then policy clamp**: the compliance ceiling sits above the
plan cap, so it clamps last.

| Site | File |
|------|------|
| Upload commit | `Features/Uploads/CommitR2/CommitR2Handler.cs` — `RetentionPolicy.Clamp(ResolveExpiry(...), user?.RetentionDays)` after `PlanTierLimits.GetMaxExpiry` |
| Extend | `Features/Deliveries/Extend/ExtendDeliveryHandler.cs` |
| Extension-request approval | `Features/Deliveries/ExtensionRequests/ApproveExtensionRequestHandler.cs` |
| Retention sweep | `Infrastructure/Jobs/DeliveryExpiryJob.EnforceRetentionAsync` — converges rows created before the policy existed |

### 3.5 Regulated mode — refuse, don't silently no-op

With `RetentionNoExtend: true`, both extend paths check `RetentionPolicy.Exceeds(...)` first
and return `retention_policy_locked` instead of extending. The reason is honesty: the clamp
would pin the new expiry back to the ceiling, and a silent no-op reads as "extended" to the
caller.

Both paths load the window off the **delivery's sender, not the caller**, and read it fresh
from the database rather than from a cached `User`. That is what makes the approval path
correct: a recipient requests the extension, the owner approves it, and the owner's policy
is the one that governs.

### 3.6 Sweep

`DeliveryExpiryJob.EnforceRetentionAsync` runs **before** the expiry pass in the same job:

1. Prefilter in SQL — `SenderId != null`, `Sender.RetentionDays != null`, `DeletedAt == null`,
   `LegalHoldAt == null`, status `Active` or `Scheduled`, and expiry beyond
   `now + RetentionPolicy.MinDays` (nothing expiring inside the shortest possible window can
   exceed a policy).
2. One set-based projection carries each owner's window along with the row, so the batch
   costs one round trip instead of a policy lookup per delivery.
3. Clamp, then evict `download:info:{shareCode}` for every row touched — the download page
   reads the expiry off that snapshot, and clamping without evicting leaves recipients
   looking at a date the row no longer carries.
4. Audit `SecurityEventType.RetentionEnforced` once per owner with the affected count; record
   one anonymous `AnalyticsEvents.RetentionEnforced` per sweep.

The clamp never lands in the past (the shortest window is a day), so the sweep pulls expiry
dates in without expiring anything itself — the expiry pass that follows does that.

### 3.7 UI

The retention card lives inside `#security-activity-card` in
`Components/Pages/AccountSecurityActivity.razor`. Direct GET renders the current policy and
preview. A CSRF-protected native form provides separate preview and save actions; preview
does not mutate the policy. Security event filters use URL-backed Unpoly links.

---

## 4. Delivery Scope Control (issues #142 + #166)

### 4.1 Fail-closed evaluation

`Infrastructure/DeliveryAccessPolicy.cs` is pure static evaluation.
`DeliveryAccessDecision` is `Allowed` | `IpNotAllowed` | `CountryNotAllowed`.

- **AND-scoped**: a caller must satisfy *every* configured rule set. Rules left unset never
  refuse anyone.
- **A malformed rule does not match.** Never "matches everything" — a typo must never
  silently open a restricted link.
- **An unverifiable origin does not pass.** When the country header is absent and a country
  rule exists, the caller is refused: a rule that exists to verify origin cannot be satisfied
  by an origin nobody can verify.
- `Evaluate` parses `203.0.113.0/24`, `2001:db8::/32`, and bare addresses (treated as
  single-host ranges), rejecting out-of-family or out-of-range prefix lengths.
- `NormalizeIpRanges` / `NormalizeCountries` run before storage: trim, upper-case country
  codes to `CountryCodeLength = 2`, de-dupe, drop unparseable entries, cap the count.
  **They return null when nothing valid remains**, so "no rule" and "only garbage" collapse
  to the same unrestricted state.

### 4.2 Storage

`Delivery.AllowedIpRanges` / `AllowedCountries` / `BlockedCountries` — comma-separated text
(`DeliveryAccessPolicy.RuleSeparator`), max `Delivery.AccessRuleMaxLength = 500` each. There
is no workspace table and no per-request policy lookup: **the delivery carries its own
policy**, so a link is self-describing and an ownership change cannot retroactively widen it.

### 4.3 Enforcement

`Features/Downloads/Shared/DeliveryAccessService.cs` (scoped, registered in `Program.cs`).
Every recipient-facing content path routes through it, so one rule set governs the page, the
byte stream, and every preview — and so a blocked attempt is always recorded.

| Consumer | File | Refusal |
|----------|------|---------|
| Download info | `Features/Downloads/Info/DownloadInfoHandler.cs` | `CheckByCodeAsync` → `delivery_restricted`, **before** the `download:info:{shareCode}` cache read |
| Byte stream | `Features/Downloads/Download/DownloadDeliveryHandler.cs` | `CheckAsync` → 403, before any password/OTP gate so the perimeter is not a password oracle |
| Inline preview | `Features/Downloads/Preview/PreviewDeliveryHandler.cs` | `CheckAsync` → `delivery_restricted` — a preview is the file |
| Office preview | `Features/Downloads/Preview/OfficePreviewHandler.cs` | `CheckAsync` → `delivery_restricted` |
| Zip entry list | `Features/Downloads/ZipContents/ZipContents.cs` | `CheckAsync` → `delivery_restricted` — the entry list is content |

Caller IP comes from `Connection.RemoteIpAddress`, already rewritten from `X-Forwarded-For`
by `UseForwardedHeaders`. Country comes from the Cloudflare `CF-IPCountry` edge header — the
same source product analytics uses.

`DeliveryAccessRules.HasRules` short-circuits the common case: a delivery with no rules
returns `Allowed` immediately, with no audit and no writes.

### 4.4 Caching constraint

The **rule snapshot** is cached 30s per share code under `download:access:{shareCode}`;
`InvalidateAsync(shareCode, ct)` drops it after a sender edits the rules.

**A per-caller verdict must never enter a shared cache.** The snapshot is rule columns only;
evaluation always runs against the live request. This is also why the scope check in
`DownloadInfoHandler` sits *before* the `download:info:{shareCode}` cache read — caching a
response that one visitor was allowed to see would hand it to the next visitor, who may not
be.

### 4.5 Refusal surface

`ErrorCodes.DeliveryRestricted`. `Components/Pages/Download.razor` renders a
`#download-phase-restricted` panel that leaks nothing — no file name, no size, no download
count — and stays `noindex, nofollow`. A refused visitor learns only that a restriction
exists. The panel works with JS off because `download.ts` hides an explicit phase-id list
rather than a wildcard, so a phase it does not know about stays visible.

Blocked attempts are audited as `SecurityEventType.DeliveryAccessBlocked` on the **sender's**
timeline: the recipient is anonymous, and the sender is who needs to see rejects.

### 4.6 Paid control

Scope rules are Pro/Studio only. **Normalisation runs first**, so an all-garbage list
collapses to null — which is "no rule" and must never trip `requires_paid_plan`. Free senders
send three nulls and never see the gate at all.

### 4.7 UI

Rule inputs on `Components/Pages/Upload.razor`, with `ts/upload.ts` `accessRule()` trimming
and nulling blank or whitespace-only values so an empty textbox never reaches the server as a
rule. Restricted panel on `Components/Pages/Download.razor`.

---

## 5. Precedence

The three controls are not peers. State this explicitly because the interaction is where a
future change is most likely to break something:

1. **Legal hold outranks the retention policy.** `EnforceRetentionAsync` excludes held rows
   in SQL. Shortening a held row's expiry would only hand a later purge an earlier date.
2. **Legal hold outranks right-to-erasure.** `PurgeDelivery` refuses with 409, and account
   deletion refuses wholesale. A court or regulator relying on those bytes outranks a
   deletion request; the hold must be lifted first, deliberately, by the owner.
3. **The retention policy outranks the plan cap** — it clamps after it, downward only.
4. **The retention policy never outranks a shorter sender choice.** Ceiling, not floor.
5. **Scope rules are orthogonal.** They gate *access*, not *lifetime*. A held delivery still
   stops being downloadable at expiry; a restricted delivery still expires on schedule; a
   scope rule neither extends nor shortens anything.

The one-line version: **hold > erasure > retention policy > plan cap > sender choice**, with
scope rules on a separate axis entirely.

---

## 6. Audit and Analytics

Every operator-visible transition lands on the account security timeline
(`SecurityEventType`, via `SecurityEventService.AppendAsync`) and, separately, as an
anonymous product event (`AnalyticsEvents` in `Features/Analytics/AnalyticsConstants.cs`).

| Transition | `SecurityEventType` | `AnalyticsEvents` |
|------------|---------------------|-------------------|
| Hold placed | `LegalHoldPlaced` | `LegalHoldPlaced` (`legal_hold_placed`) |
| Hold lifted | `LegalHoldLifted` | `LegalHoldLifted` (`legal_hold_lifted`) |
| Retention policy edited | `RetentionPolicyChanged` | `RetentionPolicyChanged` (`retention_policy_changed`) |
| Retention sweep clamped rows | `RetentionEnforced` (one per owner, with affected count) | `RetentionEnforced` (`retention_enforced`, one per sweep) |
| Scope refusal | `DeliveryAccessBlocked` (sender's timeline) | `DeliveryAccessBlockedIp` (`delivery_access_blocked_ip`) / `DeliveryAccessBlockedCountry` (`delivery_access_blocked_country`) |

Two rules hold across all of them:

- **The audit row carries what the timeline needs to stay readable.** On a lift, the case
  reference is written into the audit row *before* the column is cleared, so the timeline
  still shows what was released.
- **Analytics stay anonymous.** These events carry no user id, no email, and no raw IP —
  the scope-refusal events carry a file name and the policy-change event carries a plan, and
  nothing that identifies a person. The retention sweep records one event per sweep rather
  than one per owner precisely because the event carries no owner identity, so a row per
  owner would only duplicate it. See [analytics-architecture.md](analytics-architecture.md)
  for the anonymity model these events must not break.

The sweep has no `HttpContext` to attribute, so its audit rows record the actor as the
system — which is exactly what automatic enforcement is.

---

## 7. Error Codes

All in `Infrastructure/Constants/ErrorCodes.cs`.

| Code | Constant | Surfaces as | Raised by |
|------|----------|-------------|-----------|
| `legal_hold_active` | `LegalHoldActive` | 409 | Placing a hold on a held row; trash delete; purge; account deletion |
| `legal_hold_not_active` | `LegalHoldNotActive` | 409 | Lifting a hold on an unheld row |
| `legal_hold_reason_required` | `LegalHoldReasonRequired` | 400 | Placing a hold with a blank reason |
| `retention_days_invalid` | `RetentionDaysInvalid` | 400 | Window outside `MinDays`..`MaxDays` (PUT and preview) |
| `retention_policy_locked` | `RetentionPolicyLocked` | Failed `Result` from both extend paths | Extend or approve-extension under `RetentionNoExtend` |
| `requires_paid_plan` | `RequiresPaidPlan` | 403 | Retention PUT on a Free account; scope rules on a Free account |
| `delivery_restricted` | `DeliveryRestricted` | 403 on the byte path; failed `Result` on info/preview/zip, which the download page renders as `#download-phase-restricted` | Scope-rule refusal |

Bulk trash is the deliberate exception: a held row is counted in `skipped` rather than
failing the batch.

---

## 8. Extending This — Checklist

The invariants in section 1 only survive if new code opts into them. Two situations:

**Adding a deletion path** (anything that removes a `Delivery` row or its R2 objects):

1. Filter `LegalHoldAt == null` **in SQL** for set-based deletes, or check
   `delivery.LegalHoldAt is not null` before deleting a single row.
2. Decide the refusal shape and match the nearest existing path: single-row operation → fail
   with `ErrorCodes.LegalHoldActive` (409); batch → skip and count; job → exclude from the
   candidate query. If the path deletes bytes *and* mutates status, consider whether the
   status change is still correct on its own, the way expiry sets `Status = Expired` while
   leaving the objects in place.
3. If the operation spans many rows and is all-or-nothing (like account deletion), refuse the
   whole operation — a partial delete can orphan frozen bytes with nobody able to lift.
4. Add a case to `tests/QuickShare.Server.Tests/E2E/LegalHoldTests.cs` asserting a held row
   survives your path. The `LocalDiskStorageProviderStub` in `E2E/TestWebAppFactory.cs`
   records `DeletedPrefixes`, so you can assert which objects survived, not just which rows.
5. Add a row to the table in section 2.4.

**Adding an expiry computation site** (anything that writes `Delivery.ExpiresAt`):

1. Apply the plan cap first (`PlanTierLimits.GetMaxExpiry`), then
   `RetentionPolicy.Clamp(expiry, retentionDays)`. Policy clamps last.
2. Read the window off the **delivery's sender**, not the caller, and read it fresh from the
   database — a cached `User` can still hold the previous window.
3. If your path *extends* an expiry, also check
   `RetentionPolicy.Exceeds(current, retentionDays)` under `RetentionNoExtend` and return
   `ErrorCodes.RetentionPolicyLocked` rather than silently clamping back to the ceiling.
4. Clamp, never reject — unless the caller explicitly asked for more time, in which case
   see step 3.
5. Evict `download:info:{shareCode}` if the row is already live; the download page reads the
   expiry from that snapshot.
6. Add a row to the table in section 3.4.

**Adding a recipient-facing content path** (anything that serves file bytes, a preview, or
file structure): inject `DeliveryAccessService` and check
`CheckAsync`/`CheckByCodeAsync` **before** any cache read and before any password/OTP gate,
then add the path to the table in section 4.3.

---

## 9. Schema

One migration for all four issues:
`Data/Migrations/20260819234906_AddLegalHoldAndScopeRulesAndRetention.cs` — eight columns
plus one index.

| Table | Columns | Index |
|-------|---------|-------|
| `deliveries` | `LegalHoldAt`, `LegalHoldReason` (200), `LegalHoldById` | `IX_deliveries_LegalHoldAt` |
| `deliveries` | `AllowedIpRanges`, `AllowedCountries`, `BlockedCountries` (500 each) | — |
| `users` | `RetentionDays` (`int?`), `RetentionNoExtend` (`bool`) | — (sweep joins via `deliveries.SenderId`) |

---

## 10. Tests

| Suite | File | Covers |
|-------|------|--------|
| `LegalHoldTests` | `tests/QuickShare.Server.Tests/E2E/LegalHoldTests.cs` | Every deletion path refuses or skips a held row; place/lift lifecycle and audit; CSV report including quote escaping; list exposure; deliveries-page badge and lift button |
| `DeliveryAccessScopeTests` | `tests/QuickShare.Server.Tests/E2E/DeliveryAccessScopeTests.cs` | Scope enforcement end to end across info, download, preview, zip |
| `DeliveryAccessPolicyTests` | `tests/QuickShare.Server.Tests/Infrastructure/DeliveryAccessPolicyTests.cs` | Fail-closed rule evaluation, AND-scoping, malformed rules |
| `RetentionPolicyTests` | `tests/QuickShare.Server.Tests/Infrastructure/RetentionPolicyTests.cs` | Ceiling-not-floor clamp, preset validity |
| `RetentionPolicyApiTests` | `tests/QuickShare.Server.Tests/E2E/RetentionPolicyApiTests.cs` | Policy mutation (paid gate, range, lock cleared with window); clamp at upload and extend; regulated-mode refusal; sweep convergence and held-row exemption; direct settings-card and preview rendering |
