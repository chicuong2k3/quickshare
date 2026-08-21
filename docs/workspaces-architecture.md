# Team workspaces

Issues `#106` (team workspaces), `#252` (ownership transfer), `#145` (workspace audit log),
`#300` (admin usage report). Shipped as one subsystem because they share one migration
(`AddTeamWorkspaces`), one authorization gate and one audit store.

## What a workspace is

Three things, and nothing else:

1. **One bill.** The owner's subscription is the workspace's plan.
2. **One pooled monthly quota.** Every seat draws from the owner's `UserUsage` row.
3. **One shared delivery history.** Shares sent by a seat holder are visible to the team.

Everything below exists to make one of those three true without a second source of truth.

## Data model

| Table | Row means |
|---|---|
| `workspaces` | name, `OwnerId` (FK `Restrict`), `Seats` (2–25) |
| `workspace_members` | one seat: `WorkspaceId`, nullable `UserId`, `InviteEmail`, `Role`, `InviteTokenHash`, `InviteExpiresAt`, `JoinedAt`, `RevokedAt` |
| `workspace_transfers` | one pending handover: `FromUserId`, `ToUserId`, `TokenHash`, `ExpiresAt`, `AcceptedAt`, `CancelledAt` |

Plus three columns on existing tables: `users.PersonalPlan`, `deliveries.WorkspaceId`,
`security_events.WorkspaceId`.

**The owner holds a real seat.** Creating a workspace writes a `workspace_members` row with
`Role = Owner`. Seat counts, the audit log, the report and the transfer flow then all read one
table, instead of each remembering that the owner lives somewhere else.

**A pending invite holds its seat.** The member row is written when the invite is sent, not when
it is accepted. Without that, two admins racing for the last slot both succeed and the workspace
is oversold.

**Revoked seats are kept, not deleted.** `RevokedAt` is set and `InviteTokenHash` cleared. The
audit log keeps its subject; the slot is free immediately because every seat query filters
`RevokedAt == null`.

**One live seat per account**, enforced by a filtered unique index on
`workspace_members.UserId`. See the plan section for why.

## The plan is borrowed, not taken

An active seat copies the owner's plan onto `User.Plan` and parks the member's own plan in
`User.PersonalPlan`.

This is denormalization, chosen deliberately. Roughly twenty call sites gate features on
`user.Plan` (`SetAliasHandler`, `ExportDeliveriesHandler`, `CommitR2Handler`, …). Threading an
"effective plan" through all of them is twenty chances to miss one, and a missed one is a
member being refused a feature their workspace pays for — the kind of bug nobody reports as a
bug. Copying the value costs one column and makes every existing gate correct with no changes.

The price is that the copy has to be refreshed. It is, from exactly two places:

- `WorkspaceService.SyncMemberPlansAsync` — called on accept-invite and transfer-accept.
- `SubscriptionService.PushPlanToWorkspaceAsync` — called from all three places that write
  `User.Plan` (trial start, payment, expiry sweep).

`RestorePersonalPlanAsync` reverses it on revoke. Restoring is *not* downgrading to Free: an
account that pays for Pro personally keeps Pro when its seat ends.

The filtered unique index on `UserId` exists for this reason. Two live seats would give
`User.Plan` two masters, and the loser would be a silently wrong tier gate.

## Pooled quota

`QuotaService.ResolveBucketAsync` maps an uploader to the account whose `UserUsage` row the
upload counts against: the workspace owner for an active seat, otherwise the uploader.

Both `CheckAsync` and `RecordAsync` go through it. That is the whole point of putting it inside
the shared service rather than at the three upload call sites — a member who *checked* against
the pool but *recorded* against a private row would have an unlimited quota in practice.

## Shared history

`CommitR2Handler` stamps `Delivery.WorkspaceId` from the sender's live seat.
`ListDeliveriesHandler` widens its filter to `SenderId == me || WorkspaceId == mine`.

Read-only on purpose: every mutation path (trash, purge, extend, replace, alias, lock) still
matches on `SenderId`. Seeing a team-mate's share is not the same as being able to delete it.
A file-request submission has no sender and is never stamped.

## Ownership transfer (`#252`)

```
owner nominates a member  →  successor accepts within 7 days  →  roles swap
        (nothing moves)           (token, hashed at rest)         (audited)
```

- **Owner only.** An admin handing off someone else's bill is an escalation, not a convenience.
- **Consent is required.** Ownership carries billing, so the successor accepts or nothing
  happens. A workspace whose owner vanishes is the problem this solves; inheriting an invoice
  by surprise is not an acceptable cure.
- **One open transfer at a time.** Two live tokens race, and the second to land demotes an
  owner who was already demoted.
- **The successor's own plan is what qualifies them** (`PersonalPlan ?? Plan`) — the seat plan
  they are wearing is the one about to disappear.
- **The source owner is demoted to Admin, not removed.** An ex-owner locked out of the
  workspace they built is worse than one with rights that can be stripped afterwards, and it
  guarantees the workspace is never left unadministered.
- **Assets do not move.** Deliveries, audit history and pooled usage belong to the workspace,
  not to whoever owns it this week.

**Account deletion is refused while you own a workspace** (`workspace_transfer_required` → 409).
The FK is `Restrict`, so this is a guard with a message rather than a foreign-key crash — and
deleting the owner would take the whole team's workspace with them.

## Workspace audit log (`#145`)

The same append-only `security_events` store as the personal timeline (`#126`), stamped with
`WorkspaceId`. The admin view is a filter, not a second write path that could drift out of step
with the first. Retention is the existing 90-day `SecurityEventCleanupJob` — old rows are gone,
not hidden.

Eleven workspace event types (`WorkspaceCreated` … `WorkspaceReportGenerated`). Filters
(member, action, date range) apply in SQL; `GET /api/workspace/audit/export` honours the same
filters and emits CSV.

## Admin usage report (`#300`)

`WorkspaceReportService.BuildAsync` is the single query set behind the on-demand view, the CSV
export and the weekly email. A second implementation would be a second set of numbers, and an
admin comparing the two would be right to distrust both.

Rolling window (default 30 days) for delivery activity; the current billing month for quota,
because that is when the plan cap actually resets. **Workspace deliveries only** — personal
shares by the same accounts are out of scope, since an admin report is not a licence to read
private history (`#187`).

Reading the report is itself audited (`WorkspaceReportGenerated`); an unaudited report is a
quiet way to read every member's volume.

`WorkspaceReportJob` mails the owner weekly. Weekly is the only cadence shipped — per-workspace
scheduling would need a settings table and a per-row cron, and the on-demand endpoint covers
every other window. **Not shipped: PDF export** (CSV only) and per-member drill-down beyond the
per-member rows the report already carries.

## Authorization

One gate: `WorkspaceMember.CanAdminister` (active seat, `Owner` or `Admin`).
`WorkspaceAuditReader.ResolveAdminSeatAsync` is the single resolver both the audit and report
routes use — one gate, so JSON and CSV cannot disagree about who is an admin.
`WorkspaceStatus.For` maps every workspace error code to a status, so one route never answers
403 where the next answers 400 for the same refusal.

Roles: `Owner` > `Admin` > `Member` > `Viewer`. `Owner` is not assignable through the role
endpoint — it only moves through the transfer flow.

## Tokens

Invite and transfer tokens are generated with `ShareCodes.Generate()` and stored as SHA-256
(`WorkspaceService.HashToken`). The raw value only ever exists in the email. Accepting an
invite does not require the signed-in email to match the invited address: the token is the
capability, and a match requirement locks out anyone whose work address differs from their
sign-in one.

## UI

`/account/workspace`, `/account/workspace/audit`, `/account/workspace/report` — all HTMX
lazy-loaded per the project default, all `noindex, nofollow` (`#121`). `/workspace/join` is the
token landing page and is deliberately *not* lazy-loaded: there is no DB read to defer, so a
shell request would add a round trip and buy nothing. Styles live in `wwwroot/css/app.css`
under the `workspace-` prefix, because the markup ships inside HTMX partials where another
component's scoped CSS never applies.

## Extending this

- **New code path that reads a plan for a workspace member?** Nothing to do — `user.Plan`
  already carries the workspace plan.
- **New code path that writes `User.Plan`?** It MUST call
  `SubscriptionService.PushPlanToWorkspaceAsync` (or `SyncMemberPlansAsync` directly). A plan
  write that skips it leaves seats on a plan nobody pays for.
- **New workspace-scoped admin action?** Audit it through `WorkspaceService.AuditAsync` and add
  the event type to `AuditActions` in `AccountWorkspaceAudit.razor`.
- **New upload path?** Use `QuotaService`; do not read `UserUsage` directly, or pooling stops
  applying to it.

## Tests

`tests/QuickShare.Server.Tests/E2E/WorkspaceTests.cs` (18) — creation gating, seat holds,
seat-shrink floor, invite accept/expiry, plan borrow + restore, member-cannot-administer,
pooled quota, transfer (nomination is inert, accept moves ownership, self and non-owner
refused), owner-deletion guard, audit admin-gating + filter + CSV, report scoping + gating.
