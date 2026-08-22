# Protocol mounts (#195 WebDAV, #265 S3 gateway)

Design and invariants for mounting a workspace with tools that were never written for us.

Related: [`workspaces-architecture.md`](workspaces-architecture.md) (seats, folders),
[`developer-platform.md`](developer-platform.md) (API keys).

## Why a mount at all

The JSON API (#115) covers anybody willing to write code against it. The teams this feature is
for are not: they have a nightly job that writes to a network share, a designer who drags files
into Finder, a data pipeline that speaks S3 and nothing else. "Drop it in the folder and it
becomes a delivery" is a migration path off FTP that costs nobody a line of code.

`WorkspaceFileSystem` is the shared half — what a path *means* — so that every protocol surface
agrees about which files exist. WebDAV (#195) is the first consumer; the S3 gateway (#265) is
the second.

## What a path means

- `/dav/` is the workspace's root: its root folders, plus deliveries filed loose in the
  workspace.
- `/dav/{folder}/` is a shared folder (#151), one or two levels deep — the mount does not raise
  the depth cap.
- A file is a delivery. Its name is the delivery's file name; two deliveries with one name get
  the share code before the extension, because a folder may hold two `report.pdf` and a
  directory listing may not.

**The namespace is the workspace, never a personal pile.** A mount is a team surface: a
colleague's key must not be able to list somebody's private history as a drive. Personal
deliveries have no workspace and stay out.

**Deliveries awaiting approval (#162) are not listed.** A share under review has not been
published to anybody, and a drive is not a way around the gate.

## Reading is owner-side

A GET through a mount does not count a download, does not run the recipient gates, and does not
appear in the delivery's stats. This is the team looking at its own file; charging it to the
recipient statistics would corrupt the only number the sender actually asked for — *how many
times did the recipient fetch this*.

## Writing goes through the same rules as the form

`PutAsync` applies the plan's file-size cap (on what landed, not on what was declared — a
chunked PUT has no length to check up front), the monthly quota, the storage region (#119), and
the folder's locked defaults (#245): expiry, privacy mode, required approval. A folder whose
locked policy requires a password refuses a mount write outright — the protocol has no field for
one, and inventing a password leaves nobody to tell it to.

**PUT over an existing name replaces that delivery**, keeping its share code, password and
expiry. That is both what a filesystem client means by PUT and what the product already calls
"replace file, keep link" (#72). Creating a second file with the same name would break every
sync client that has ever existed.

**Delete is soft** (#99) and refuses a delivery under legal hold (#266) or checked out by
somebody else (#308) — the same refusals the web UI gives, with WebDAV's own 423 for the lock.

## Credentials

An existing API key (#115), sent as HTTP Basic with the key in the password position and the
username ignored. No second kind of secret and no second place to revoke; Basic because it is
the only authentication every filesystem and object-storage client speaks.

Scopes map to what the verb does, not to how it feels: `read` for PROPFIND and GET, `write` for
PUT and MKCOL, **`manage` for DELETE** — a key a nightly job uses to drop a report should not be
able to empty the drive.

There is no anonymous mount and there never will be: a drive with no credential is a workspace's
files on the open internet.

## WebDAV specifics

Class 1 only. `OPTIONS` advertises `DAV: 1` and no `2`, because LOCK is not implemented — and a
fake LOCK that hands back a token while locking nothing is worse than none, since it tells a
client its exclusive access is real. Consequences, stated plainly:

| Client | Read | Write |
|---|---|---|
| rclone, davfs2, Cyberduck | yes | yes |
| Windows Explorer, macOS Finder | yes | no — both require locking to write |

`PROPFIND` with `Depth: infinity` is refused with `propfind-finite-depth` rather than walked: one
accidental client setting must not walk a two-hundred-folder workspace. `MKCOL` with a body is
refused (415) rather than having its requested properties silently dropped. `DELETE` on a
collection is refused — it would cascade over deliveries the client never listed, and the API
and UI are where "this folder is not empty" can be said.

Refusals map to statuses a client can act on: 423 for lock or hold, 507 for quota, 413 for the
size cap, 403 for scope and folder policy. Never `Results.Forbid()` — that runs the cookie
scheme's forbid handler and redirects a filesystem client to a sign-in page it will never
render.

## Not shipped

- LOCK/UNLOCK, and therefore read-write mounting from Explorer and Finder. #308 already models a
  checkout lock and is the honest thing to map a real LOCK onto; lock-null resources (a lock on
  a path that does not exist yet) are the work.
- MOVE and COPY (rename is a delivery rename plus a folder move — two API calls that exist, and
  a mount verb that has to be atomic across both).
- PROPPATCH: there are no dead properties to store, and pretending to store them is a lie a sync
  client will act on.
- The S3 gateway (#265) itself — the filesystem half is built and shared, the SigV4 half is not.

## Test note

`LocalDiskStorageProviderStub` now actually holds the bytes it is given. It used to discard
them, which made every stream-in/stream-out path untestable while the suite stayed green — the
same class of defect the batch-11 audit found in the object *key*. A stub must produce the same
shapes as production or it is testing itself.
