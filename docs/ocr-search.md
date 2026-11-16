# OCR search (#243)

Design and invariants for reading the text inside images so it can be found.

## Why it exists

Search covers file names and notes. The information people actually look for is often *in* the
picture — a screenshot of an invoice, a photo of a whiteboard, a scanned receipt. "The invoice
with the PO number" is unfindable until the words inside it are indexed, and scanned documents
are the norm in the market this product sells into.

## Opt-in, per file, and never in privacy mode

Reading somebody's screenshot is a data touch. It happens only when the sender ticks the box on
that upload, only for raster images, and never when privacy mode (#187) is on — a share that
deliberately keeps no record must not grow a searchable transcript of itself instead.

The privacy-mode gate is applied **at commit**, on the flag itself, rather than inside the job.
The column on the row is then already the truth, and a future second consumer of it inherits the
rule instead of having to remember it.

## The engine is the installed binary

`TesseractOcrEngine` runs `tesseract` as a subprocess. Not the interop NuGet package: that drags
native leptonica and tesseract libraries per platform and pins their versions to ours, where a
process is one apt line in the Dockerfile and nothing in the build. A host without the binary
reports `IsAvailable = false`, the job does nothing, and the feature is simply absent.

**Nothing leaves the machine.** That is the reason for a local engine over a cloud vision API:
the privacy page says files are not shipped to third parties, and an OCR feature that did would
quietly make that untrue.

ponytail: one image per process launch. This runs from a background job over files that were
just uploaded, not on a request path, so the ceiling costs nothing today. If throughput ever
matters the upgrade is a persistent worker, not a different library.

## The job

Every minute, ten at a time, oldest first. It writes a `delivery_ocr_texts` row **either way** —
`Text` null means "we looked and found nothing", and that row is what stops the same unreadable
screenshot being re-processed on every tick forever.

Images over 20 MB are skipped rather than queued: a scan is not a search index, and the cap is
what keeps one enormous file from occupying the loop.

Text lives in its own table rather than on `Delivery`, because the delivery row is read on every
list, every download and every recipient page, and a kilobyte of scanned invoice riding along on
all of them would be paid for constantly to serve the one query that wants it.

## Search says why it matched

One search box, one predicate: the term matches the file name **or** the text inside the image.
A separate "search inside images" toggle would make people choose which search to run before
they know which one will find it.

Results that matched on image text carry an `in image` badge. Without it, an OCR hit looks like a
name match that is not there, and the search looks broken.

## Not shipped

- PDF text extraction. A PDF carries its own text layer; that is a different path, not this one
  pointed at a different content type.
- Re-indexing on demand, and indexing files uploaded before the sender changed their mind. The
  flag is decided at upload; a "index this now" button is a small endpoint away when somebody
  asks for it.
- Highlighting the matched region inside the preview — the box coordinates are available from
  tesseract, but the preview is an `<img>` with no overlay to put them on.
- Language detection. `eng+vie` is configured; a third language is an apt line and a constant.
