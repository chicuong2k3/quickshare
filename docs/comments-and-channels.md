# Delivery comments and chat channels

Issues `#289` (comments on a delivery) and `#180` (post deliveries to Slack/Teams). Both are
about the conversation around a share: one keeps it attached to the file, the other pushes it
into the room where the team already is.

## Comments (`#289`)

A share is one-way by default. A client who wants to ask "is section 3 final?" leaves the
product to do it, and the answer ends up in somebody's inbox with no connection to the file.

### Three gates, all of which only close

| Gate | Set by | Effect |
|---|---|---|
| `Delivery.CommentsEnabled` | sender, at upload | opens the thread; **off by default** |
| `Workspace.CommentPolicy` | workspace admin | can close a thread the sender opened |
| `Delivery.PrivacyMode` (`#187`) | sender | closes it outright |

None of them can open a conversation the sender did not. Privacy mode is decisive: a share that
deliberately keeps no download record should not accumulate a public thread instead, so
`CommentsEnabled` is forced false at commit when privacy mode is on rather than being resolved
later — the column reflects reality.

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

- **New comment surface?** Go through `CommentGate.EvaluateAsync`. The three gates are not
  reproducible by hand, and a path that checks only `CommentsEnabled` ignores workspace policy.
- **New channel event?** Add a method to `ChannelNotifier` and a flag on the hook. Keep it
  best-effort and keep the timeout.
- **Never return `WebhookUrl`** from any endpoint, and never put it in an audit payload.
- **Test stubs must produce the same shapes as production.** If a stub invents a value, it is
  hiding whatever reads that value.

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
