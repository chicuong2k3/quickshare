# Delivery comments and chat channels

Issues `#289` (comments on a delivery) and `#180` (post deliveries to Slack/Teams). Both are
about the conversation around a share: one keeps it attached to the file, the other pushes it
into the room where the team already is.

## Comments (`#289`)

A share is one-way by default. A client who wants to ask "is section 3 final?" leaves the
product to do it, and the answer ends up in somebody's inbox with no connection to the file.

### Five gates, all of which only close

| Gate | Set by | Effect |
|---|---|---|
| `Delivery.CommentsEnabled` | sender, at upload | opens the thread; **off by default** |
| `Workspace.CommentPolicy` | workspace admin | can close a thread the sender opened |
| `Delivery.PrivacyMode` (`#187`) | sender | closes it outright |
| delivery lifecycle | — | expired, revoked, suspended or awaiting approval closes it |
| scope rules (`#142`/`#166`) | sender | a caller refused the file is refused the thread |

None of them can open a conversation the sender did not.

**The last two were missing when this shipped.** Comments are a recipient-facing content path,
and the project's own rule is that every such path routes through `DeliveryAccessService` — this
one did not. A caller blocked by an IP allowlist or a country rule could read and post to the
discussion about a file they could not open, and an expired or revoked share kept serving its
thread. `CommentGate` now applies both before anything else. See the audit note below.

Privacy mode is decisive: a share that deliberately keeps no download record should not
accumulate a public thread instead, so `CommentsEnabled` is forced false at commit when privacy
mode is on rather than being resolved later — the column reflects reality.

`CommentPolicy.MembersOnly` restricts **posting**, not reading. Recipients can still follow the
thread; that is the distinction the policy is actually asked for.

### Anonymous authors

Recipients have no account, so an anonymous author supplies a display name and is identified for
abuse purposes by a hashed IP — the same treatment every other address gets (`#26`).

**A supplied name is a claim, not an identity.** The thread marks which authors are signed-in
accounts (`IsTeamMember`) so a reader can tell the difference rather than being invited to
assume one. Anonymous posting is rate-limited per delivery per address per hour; signed-in
authors are already accountable and are not.

### Deleting

Soft, and the body is **blanked**, not merely flagged. The row stays so a reply does not lose the
message it answers; keeping the text around is how a "delete" turns out not to have been one.

The delivery's sender may remove anything on their share; a signed-in author may remove their
own. An anonymous author **cannot** — "same hashed address" is not proof of authorship, and
honouring it would let anyone behind the same NAT delete somebody else's words.

### Depth

One level of replies. Deeper threads need collapsing UI nobody has asked for, and the same
reasoning caps folder nesting at two.

## Chat channels (`#180`)

Enterprise recipients live in Slack and Teams. A file-sharing tool that never reaches the channel
loses the collaboration slot to one that does.

### One body shape for both platforms

A plain `{"text": …}` POST, which a Slack incoming webhook and a Teams connector both accept.
Per-platform adaptive cards would be two renderers to keep in step for a difference a channel
reader would not notice.

### The webhook URL is the credential

Anyone holding it can post into that channel. So it is stored, **never returned** (the API
reports only that one is set), blank-on-save leaves the stored value alone, and the audit entry
records the **host**, not the URL — an audit trail that captures the credential is a second place
the credential lives.

HTTPS only.

### What gets posted

New deliveries (`NotifyOnDelivery`) and comments (`NotifyOnComment`).

A share held for approval (`#162`) is **not** announced at commit — it is announced when the
reviewer clears it, for exactly the reason its recipient emails are held: a channel post is how
somebody outside first learns the link exists.

### Posting never breaks a share

Every post is best-effort inside a `try/catch` with a 5-second timeout. A chat outage must not
surface as a failed upload or a refused comment.

The trade is latency: the post happens inline, so a slow channel adds to the request rather than
to a queue. That is deliberately the same pattern the recipient emails in the same handler
already use; a background queue would be new infrastructure for a call that normally takes tens
of milliseconds.

A hook that fails ten times running is **parked** — a revoked webhook returns the same error
forever, and retrying it on every upload is latency spent to learn nothing. Saving the hook
clears the counter.

## Audit findings (2026-08-22)

An audit of the tier-6 batches found three defects in this subsystem's own code, all of the same
species: **something written, documented, and never actually reached.**

1. **Comments bypassed every delivery gate.** No scope rules, no lifecycle check — described
   above. This is the serious one: it violated an invariant already written into `AGENTS.md` by
   an earlier batch, which is worse than not having thought about it.
2. **`ChannelNotifier.NotifyDeliveryAsync` was never called.** The method existed, the
   `NotifyOnDelivery` flag was settable and documented, and posting a *new delivery* — the
   headline of `#180` — did nothing. Only comments posted. Now wired into commit and into
   approval release.
3. **`BillingAccountResolver` was dead code**, DI-registered and documented as "the one place
   that resolves whose bill a member's charges land on", while `QuotaService` did its own
   resolution. Deleted.

The common cause is that unit tests prove a function computes the right answer and say nothing
about whether anything calls it. `AuditRegressionTests` is the counterweight: every case there
asserts the *reaching* rather than the shape, using an observable side effect (an unreachable
webhook leaving a failure counter behind) rather than a mock.

## Two defects this batch found in `#119`

Both were shipped in the residency batch and both were invisible to a green suite.

1. **`Delivery.StorageRegion` was never assigned.** The edit that stamped it at commit silently
   failed to apply. Key-derived *routing* worked, so bytes went to the right bucket — but the
   recorded region was always null, which meant the recipient badge never appeared and the
   replace/clone/recurrence paths all passed null onward.

2. **Regions were discarded on config fallback.** `ObjectStoreResolver.Resolve` returned
   `ObjectStoreOptions.FromR2(...)` when the `ObjectStore` section was not fully configured, and
   that projection dropped the `Regions` table. A deployment still configured under `R2` that
   added `ObjectStore:Regions` would have had residency silently disabled.

**Why the suite missed both:** the test factory's storage stub returned a made-up key
(`stub/{id}/{name}`) instead of the real shape, so nothing downstream could read a region out of
it, and no test configured any regions. Both are fixed — the stub now calls `R2ObjectKey.For`,
and the factory configures two regions — so residency is exercised end to end rather than only
in unit tests of its parts.

That is the lesson worth keeping: **a stub that diverges from the real shape makes an entire
feature untestable without anything looking wrong.**

## Extending this

- **New comment surface?** Go through `CommentGate.EvaluateAsync`. The five gates are not
  reproducible by hand, and a path that checks only `CommentsEnabled` ignores workspace policy.
- **New channel event?** Add a method to `ChannelNotifier` and a flag on the hook. Keep it
  best-effort and keep the timeout.
- **Never return `WebhookUrl`** from any endpoint, and never put it in an audit payload.
- **Test stubs must produce the same shapes as production.** If a stub invents a value, it is
  hiding whatever reads that value.

## The screens

Both surfaces now exist. They were built after the audit found this subsystem was API-only.

**Comment thread** — server-rendered inside `Download.razor`, below the file card. It is not
fetched: a recipient with scripting off still reads the conversation, because the conversation is
the part that carries the information. Only the posting controls need JS, and they are marked
`.dl-comments__js`, hidden by a `<noscript>` style block. The page resolves what to show through
`CommentGate` — the same object the endpoint uses — so the page cannot display a thread the API
would refuse. One level of replies is rendered; deeper nesting is a threading model nobody asked
for. `wwwroot/js/comments.js` reloads after a successful post rather than splicing the new
comment in, because ordering, tombstones and the team badge are server decisions and re-deriving
them in the browser would be a second implementation to keep in step.

**Channel config** — `/account/workspace/channel`, admins only, `noindex`. The webhook field is
never populated with the stored URL; when a hook exists the placeholder says one is stored and a
blank submit keeps it. That is asserted by `B2BUiTests.ChannelPage_ShowsStoredHookWithoutLeakingUrl`, which posts a recognisable secret and greps the rendered page for it. The health card shows
last-posted, consecutive failures and the parked notice.

Sender-side, `Upload.razor` now has the `upload-comments` toggle that sets `CommentsEnabled` at
commit. Before it, the flag existed on the entity and in the API and no sender could set it.

## Not shipped

- **Comment attachments.** `#289` mentions them; they need an upload path with its own quota,
  scanning and retention story, which is a second file feature hiding inside a comment box.
- **@-mentions with notification routing.** The sender is notified of every comment on their
  share, which covers the case the issue actually describes; mention parsing needs a participant
  directory that recipients are not in.
- **Download events posted to the channel.** Deliveries and comments are wired; downloads are
  already emailed, and a third trigger is more surface for little added proof of the concept.
- **Comment export in the GDPR data export.** Comments cascade-delete with their delivery and are
  covered by legal hold through it, but they are not yet in the export payload.

## Tests

`tests/QuickShare.Server.Tests/E2E/CommentsAndChannelTests.cs` (18) — the region regression that
started this batch (an upload records the region it was pinned to, and records none when it was
not); comments closed by default, closed by privacy mode, closed by workspace policy,
members-only restricting posting but not reading; anonymous authors needing a name and being
marked as unverified; replies one level deep; delete keeping the row and removing the words;
delete refused to a stranger; unknown share code 404. Channel: plaintext and unknown platform
refused, first save without a URL refused, URL write-only and preserved on blank save, audit
recording the host and not the token, an unreachable webhook breaking neither an upload nor a
comment, admin gating, and delete.
