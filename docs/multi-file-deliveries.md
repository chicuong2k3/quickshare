# Multi-file deliveries

Issues `#79` (multiple files in one delivery, download-all-as-zip) and `#43` (folder upload).
The WeTransfer-parity headline, and the largest shape change to `Delivery` since it was written.

---

## The invariant everything rests on

**Every delivery has at least one `delivery_files` row — including every single-file delivery
that predates the feature.** The migration backfills them.

That is not tidiness. Without it, every consumer would have to ask "is this the old shape or the
new one?", and the ones that forgot would show an empty file list for the majority of deliveries
in the system. One rule, no exceptions, no fallbacks to write.

## Why `Delivery` still has `FileName`

Because the change is additive on purpose.

`Delivery.FileName`, `ContentType`, `FileSize` and `StoragePath` keep describing **file #1**,
exactly as before. Two new columns describe the bundle: `FileCount` and `TotalFileBytes`.

The payoff is that every path written before multi-file existed — previews, virus scan, expiry,
backup, SIEM shipping, the download page's single-file phase, the P2P flow — keeps working
untouched, and keeps being *correct* for the single-file case that is still almost every
delivery. Nothing had to be found and changed. New code reads the rows; old code never noticed.

The cost is one duplicated fact (file #1 appears twice). It is written in one place, at commit,
and the tests assert the two agree.

## Upload

1. `POST /api/uploads/start-r2` — unchanged. Creates the session; this is file #1.
2. `POST /api/uploads/{deliveryId}/files` — presigned parts for each extra file.
3. `POST /api/uploads/commit-r2` — now accepts `additionalFiles[]`.

Splitting it this way keeps the single-file path byte-for-byte what it was: the common case pays
nothing for a feature it does not use.

### The security property

**A committed object key must belong to the delivery claiming it.** `R2ObjectKey.BelongsTo`
compares the delivery-id segment of the key, and `BundleCommitter` refuses anything else.

The commit request is client-supplied. Without that check, a caller could name *any* key in the
bucket — including one from somebody else's delivery — and have it served under a share code they
control. `MultiFileDeliveryTests.Commit_RefusesAnObjectKeyFromAnotherDelivery` does exactly that
with a real victim key and requires a non-200.

Two more, for the same reason:

- **Adding a file requires owning the session.** Otherwise anyone could staple a file onto
  somebody else's upload and have it delivered under their name.
- **Quota counts the whole bundle**, not file #1. Charging only the first would make a
  hundred-file upload free after the first file, which is the obvious way around a monthly cap.

A bundle is also all-or-nothing: if any extra fails, everything under the delivery's prefix is
deleted and the commit is refused. A half-committed bundle is worse than none, because the
recipient cannot tell which it is.

## Filenames

`DeliveryFile.SanitizeName` strips absolute paths, `..` segments, backslashes, drive letters and
control characters, and it runs **twice** — at add-file time and again at commit, because the
commit request is its own trust boundary.

This is zip-slip defence. An entry named `../../etc/passwd` escapes the extraction directory in a
careless client. We cannot fix every client; we can refuse to be the one that ships the payload.

Forward slashes survive on purpose: that is what makes folder upload (`#43`) work. The browser
puts a relative path in `webkitRelativePath`, it is stored as the filename, and the zip rebuilds
the folder the sender dragged in.

## Download

| Route | Purpose |
|---|---|
| `GET /api/downloads/{code}/files` | List the files |
| `GET /api/downloads/{code}/files/{id}` | One file, via a 5-minute presigned redirect |
| `GET /api/downloads/{code}/zip` | Everything, as one streamed archive |

All three go through `BundleAccess.ResolveAsync` — one gate, used by all three, because a gate
applied in two of three is not a gate. **A password- or OTP-protected share is not listable
either**: names, sizes and folder structure are precisely the information the gate exists to
withhold.

### The zip

Streamed through a `Pipe`, not buffered. A bundle can be tens of gigabytes; building the archive
in memory or on disk first would make the server's headroom the real cap on delivery size.

Entries are **stored, not deflated**. The payloads are usually already-compressed media, so
deflating spends CPU to make the output marginally larger, and storing lets bytes flow straight
from object storage to the client.

Two things learned the hard way and worth not re-learning:

- **Writing to `Response.Body` directly does not work under FastEndpoints.** The framework still
  owns the response, so those bytes are discarded and the caller gets an empty `200` — which
  presents as a corrupt archive, not as a bug in the endpoint. Producing into a pipe and handing
  the reader to `Send.StreamAsync` keeps the framework in charge while memory stays bounded.
- **Duplicate entry names silently lose files.** Two files can legitimately share a name —
  different folders, or a sender who picked the same one twice — and most extractors write the
  first and drop the rest. `UniqueEntryName` suffixes them the way a file manager would.

Honest trade: no `Content-Length`, so no browser progress bar, and a mid-stream failure arrives
as a truncated archive rather than an HTTP status. That is what every streaming-zip endpoint
signs up for, and it beats capping bundle size at whatever fits in RAM.

## The recipient page

The file list is **server-rendered**, like the comment thread and for the same reason:
`DownloadNoScriptTests` requires the page to work with scripting off, and a bundle whose contents
only appear after a fetch is an empty page to that reader.

A single-file delivery renders exactly as it did — no "1 file" list, no "download all" button.
One file is not a bundle.

## Invariants for anyone changing this

- **Never accept an object key without `R2ObjectKey.BelongsTo`.**
- **Never let the file list skip the access gate** that the download itself goes through.
- **Keep `Delivery`'s columns describing file #1.** The moment they describe the bundle instead,
  every pre-multi-file path silently starts lying.
- **Sanitise filenames at every boundary they cross**, not once at the first one.
- **Charge quota on the total**, never on file #1.

## Not shipped

- **Per-file expiry or per-file access rules.** A delivery is the unit a recipient is given; a
  bundle where half the files have gone is a support conversation.
- **Adding files to an already-committed delivery.** That is versioning (`#110`), not bundling —
  it changes what a link means after somebody has been given it.
- **Re-ordering after upload.** The sender's selection order is the order; a reorder UI is `#275`.
- **Zip64.** The archive format's 4 GB per-entry limit applies. Large single files are already
  better served by the direct per-file download, which has no such limit.
- **Bundle-aware previews.** The preview paths still act on file #1, which is the correct
  behaviour for the single-file case and a reasonable default for a bundle.

## Tests

`tests/QuickShare.Server.Tests/E2E/MultiFileDeliveryTests.cs` (20).

The invariant, the security check, the zip's behaviour on duplicate names and folder paths, the
filename sanitiser's traversal cases, and — importantly — that a single-file delivery still gets
its row and still renders unchanged. The last one is what proves the change was additive rather
than merely tested in isolation.
