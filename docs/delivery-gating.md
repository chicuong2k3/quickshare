# Delivery gating and recurrence

Issues `#162` (approval workflow) and `#176` (recurring delivery). Both answer the same
question from opposite ends: **when does a link actually go out?** One holds it back until
somebody says yes; the other sends it again, on a cadence, without being asked.

## Approval workflow (`#162`)

### Ordering is the feature

A gate that mails the link and *then* asks permission is not a gate. So at commit time, when a
share is held:

- `Status` becomes `PendingApproval`
- the recipient list is parked on `Delivery.PendingRecipients` instead of being mailed
- the **sender's own** link email is withheld too — a mail saying "your file is ready to share",
  for a link that refuses everyone, is worse than no mail
- both recipient-facing paths return **404**

Approving is the moment the outside world first learns the link exists. The held recipients are
mailed *there*, from `ApprovalNotifier`, which exists precisely so the withholding and the
release live next to each other.

### The 404 is deliberate

`PendingApproval` reports as `DeliveryNotFound` on both `/api/downloads/{code}/info` and the byte
stream — not as a distinct "awaiting approval" state.

Whether a sender's organisation reviews its shares is a fact about **the sender's organisation**,
not about the recipient. A recipient who was sent the link early should learn nothing from it,
and a distinct status code would turn the endpoint into an oracle for internal process.

### Where the requirement comes from

Two sources, and it only ever tightens:

| Source | Meaning |
|---|---|
| `Workspace.RequireDeliveryApproval` | every share from this workspace |
| `WorkspaceFolder.DefaultRequireApproval` | shares filed under this folder (or its parent) |

The folder flag rides the existing `#245` inheritance chain and is OR-ed like the other
protections, so a folder can require approval where the workspace does not — never the reverse.

It is deliberately **independent of `DefaultsLocked`**: a review gate a sender can switch off is
not a gate, so unlike the prefill settings this one binds whether or not the defaults are locked.

**Personal deliveries are never gated.** Approval is a workspace control, and a share with no
workspace has no reviewer it could be waiting on.

### Decisions

- **Self-approval is off by default** (`Workspace.AllowSelfApproval`). The control exists because
  somebody wanted a second pair of eyes, and one's own eyes are not a second pair.
- **Denial requires a reason.** "Denied" with no explanation sends the sender back to guess, and
  the guess is usually to re-upload the same file.
- **Denial goes to trash, not destruction.** A wrong call stays recoverable inside the grace
  window.
- **Approving twice is a 409.** The second approval would re-send mails that already went.
- **The queue is a query, not a table.** A delivery in `PendingApproval` *is* the pending item; a
  parallel queue table would be a second place for the two to disagree. Ordered oldest-first,
  because a queue that surfaces the newest first leaves the oldest waiting longest.
- Approving a share with a future `DeliverAt` returns it to `Scheduled`, not `Active` — clearing
  a review does not override the sender's send time.

## Recurring delivery (`#176`)

Scheduled send (`#69`) is one-shot: at time T, go live, done. This is the other ask — "Monday's
report to the same five people, every week".

### A fresh link every cycle

Each run copies the source delivery's bytes to a **new object with a new share code**, rather
than re-mailing the original link.

A link that never rotates is a link that leaks: last week's recipients should not be able to open
this week's numbers by revisiting an old email. The copy stays in the source's storage region
(`#119`), and legal hold, approval state and trash flags are explicitly *not* carried over — a
generated cycle is a new share, not a clone of the original's compliance state.

A generated cycle is **never itself gated for approval**. The schedule was approved when it was
created; re-gating every cycle would make recurrence useless in exactly the workspaces that
turned approval on.

### Catch-up is skipped, not batched

`ComputeNextRun` always computes strictly forward from *now*, never by adding one interval to the
missed slot. A service that was down for a week sends **one** report on restart, not seven.
Recurring features that storm on recovery become incidents of their own.

The next run is advanced **before** the attempt, so a schedule that throws does not retry every
hour forever against the same failure.

Monthly day-of-month is clamped to 28. A "31st" schedule that silently skips February is a
schedule that has quietly stopped being monthly.

### Decisions

- **Skip and pause are separate.** "Not this week" is a one-off; "not for now" is indefinite.
  Collapsing them would force the owner to choose between losing the schedule and sending
  something they did not want to send.
- **Resuming re-bases on now**, so a schedule paused for a month does not fire the instant it is
  switched back on.
- **A missing source disables the schedule with a readable reason** rather than firing empty
  cycles forever.
- **Recipients are normalised** (lowercased, de-duplicated) on save, so the mail loop cannot send
  twice to one address.
- **Run-now shares the scheduled method** and advances the schedule — a "test" that fires again
  an hour later is a duplicate the recipients have to reconcile.
- Deleting a schedule leaves the deliveries it generated alone: they are real shares recipients
  may still be using.

## A defect this batch fixed

`CloneDeliveryHandler` built its new key with `R2ObjectKey.For(..., fileName)` — no region. After
`#119`, cloning a region-pinned delivery silently moved the copy to the default bucket, across a
jurisdiction boundary. It now passes `delivery.StorageRegion`, and the recurrence job does the
same for every cycle it generates.

That is exactly the failure mode the `#119` AGENTS.md rule warns about, found by writing the next
feature against it rather than by anybody noticing in production.

## Extending this

- **New path that publishes a delivery to recipients?** Check `PendingApproval` first. The
  status column is not self-enforcing.
- **New recipient-facing endpoint?** `PendingApproval` must be indistinguishable from
  not-found. Do not add a status code for it.
- **New code that copies a delivery?** Carry `StorageRegion`, and do *not* carry legal hold,
  approval state or `DeletedAt`.
- **New workspace-wide policy?** Follow the tighten-only rule: workspace OR folder, never a
  folder loosening the workspace.

## Not shipped

- **An approval SLA timer with escalation to a second approver.** `#162` lists it; it needs an
  escalation chain and a notion of "next approver" that the seat model does not have, and a
  timer that escalates to nobody is worse than no timer.
- **Admin UI switches for `RequireDeliveryApproval` / `AllowSelfApproval`.** The columns and the
  enforcement are real and tested; the settings toggles are not built, so today they are set
  directly. Flagged on the issue rather than left to be discovered.
- **Content refresh per cycle beyond replace-file.** Replacing the source file (`#72`) already
  changes what the next cycle sends, which is the behaviour the issue describes; there is no
  separate versioned-content pipeline.

## Tests

`tests/QuickShare.Server.Tests/E2E/ApprovalAndRecurringTests.cs` (19) — commit holds the share
and parks its recipients; both recipient paths 404; approve activates and releases; deny requires
a reason and trashes; self-approval refused then allowed; double-approve 409 and non-admin
forbidden; queue ordering and workspace scoping; folder-level tightening of an ungated workspace;
personal deliveries never gated. Recurrence: forward-only next run, missed cycles skipped,
monthly clamped, due-check, plan and source validation, recipient normalisation, fresh share code
per run, skip/pause/resume semantics, self-disable on a missing source, and owner scoping.
