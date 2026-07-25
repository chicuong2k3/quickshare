# Adversarial Review — TeleCloud R2 Storage Integration

**Reviewer:** Adversarial divergence simulator
**Spine:** `ARCHITECTURE-SPINE.md`
**Date:** 2026-07-20
**Verdict: FLAG**

---

## Method

Two independent developers were simulated:

- **Dev A** — owns `StartR2UploadHandler`, `InitiateUploadAsync`, `CompleteUploadAsync`, `CommitR2UploadHandler`. Writes `Delivery.StoragePath`.
- **Dev B** — owns `DownloadDeliveryHandler`, `GetDownloadUrlAsync`, `DeleteObjectAsync`. Reads `Delivery.StoragePath`.

Each obeys every AD to the letter. The seams below are the resulting incompatibilities.

---

## Divergence 1 — Object key contract: no shared constant, no normalization rule

### ADs obeyed
- AD-4: key = `uploads/{userId}/{deliveryId}/{fileName}`
- AD-7: `Delivery.StoragePath` stores "full R2 object key"

### What Dev A does
```csharp
// StartR2UploadHandler.cs
var key = $"uploads/{userId}/{deliveryId}/{fileName}";
// Stores in DB:
delivery.StoragePath = key;
```

### What Dev B does
```csharp
// DownloadDeliveryHandler.cs
// Reads Delivery.StoragePath from DB
var key = delivery.StoragePath; // "uploads/abc/def/report.pdf"
// Calls GetDownloadUrlAsync(key)
```

### Where they silently diverge

| Dimension | Dev A | Dev B | Impact |
|-----------|-------|-------|--------|
| **Leading slash** | `uploads/...` (no `/`) | Could prepend `/` because R2 CLI examples use `/uploads/...` | Key mismatch — 404 |
| **FileName encoding** | Stores raw `fileName` as-is (e.g., `my file.pdf`) | URL-encodes `fileName` before `GetObjectAsync` (`my+file.pdf`) | 403/404 on spaces, unicode, or special chars |
| **Case normalization** | Writes `uploads/...` (lowercase per convention) | Uses `objectKey` as retrieved from DB, but AWS SDK `GetObjectAsync` is case-sensitive | Silent failure on mismatched case if convention isn't enforced at storage time |
| **File extension** | Stores `report.pdf` from user upload | Expects `.pdf` but does extension-based behavior | Works, but fragile |

### Root cause
**No AD mandates a shared `R2ObjectKey` utility** that both sides call. The key format is described identically in AD-4 and AD-7 but expressed as a convention string, not a binding API. Without a single `R2ObjectKeyBuilder.Build(userId, deliveryId, fileName)` or equivalent, the two teams will inevitably diverge on encoding, normalization, prefixes, slashes, and special-character handling.

AD-4 says the rule but does not say "this format **must be produced and consumed through a single shared function**."

---

## Divergence 2 — `uploadId` is an untyped ghost between Start and Commit

### ADs obeyed
- AD-2: `POST /api/uploads/start-r2` returns `{uploadId, parts: [{partNumber, signedUrl}]}`
- AD-5: `Task<UploadInitResult> InitiateUploadAsync(...)`, `Task<UploadCompleteResult> CompleteUploadAsync(uploadId, parts)`

### What Dev A does
```csharp
// R2StorageProvider.cs
public async Task<UploadInitResult> InitiateUploadAsync(...)
{
    var r2Upload = await s3Client.CreateMultipartUploadAsync(bucket, key);
    return new UploadInitResult
    {
        UploadId = r2Upload.UploadId, // R2's multipart upload ID: "TExBMEs5Z...2VpZA=="
        Parts = presignedUrlParts
    };
}
```

### What Dev B does
```csharp
// CommitR2UploadHandler.cs
var uploadId = request.UploadId; // "TExBMEs5Z...2VpZA=="
var result = await storageProvider.CompleteUploadAsync(uploadId, request.Parts);
```

### Where they silently diverge

| Dimension | Dev A | Dev B | Impact |
|-----------|-------|-------|--------|
| **What IS uploadId?** | R2's AWS upload ID (base64-encoded opaque string) | Also uses raw R2 upload ID | Works for multipart, but... |
| **Single-PUT path** | Returns a `Uri` (presigned URL) instead of an `uploadId` — but AD-5 groups single-PUT and multipart into one method | Expects `uploadId` to always be present | Runtime cast/switch failure |
| **uploadId persistence** | Does NOT store uploadId in DB (not mentioned in any AD) | Needs to look up uploadId from DB during `CompleteUploadAsync`, but it's not there | CompleteMultipartUploadAsync receives wrong or null uploadId |
| **uploadId lifetime** | Assumes uploadId lives until `CompleteMultipartUploadAsync` is called | Doesn't know when uploadId expires or if it needs explicit abort | Orphaned multipart uploads accumulate in R2 |

### Root cause
**No AD defines what `uploadId` IS, what type it has, whether it needs persistence, or how long it lives.** AD-5 shows the method signatures as design pseudocode (parameter names without types), and the Deferred section explicitly says "exact method signatures emerge during implementation." This means two developers implementing the two sides of the `uploadId` handoff have zero AD-level guidance.

If Dev A builds `UploadInitResult` with a `string UploadId` that is the raw R2 multipart upload ID, and Dev B later needs to pass it back to `CompleteMultipartUploadAsync`, they'll couple accidentally. If Dev A needs to persist `uploadId` to survive a server restart between Start and Commit (which CQRS handlers in different HTTP requests would require), but no AD mandates that, it won't be there when Dev B's `CompleteUploadAsync` runs.

---

## Divergence 3 — `Delivery.StoragePath` write-timing ambiguity

### ADs obeyed
- AD-7: "New `Delivery.StoragePath` (nullable string) stores full R2 object key [...] Null = local disk backward compat."

### What Dev A does
```csharp
// StartR2UploadHandler.cs — writes StoragePath IMMEDIATELY at init time
delivery.StoragePath = $"uploads/{userId}/{deliveryId}/{fileName}";
await db.SaveChangesAsync();
// Then initiates multipart upload with R2
```

### What Dev B does
```csharp
// DownloadDeliveryHandler.cs — reads StoragePath
var delivery = await db.Deliveries.FindAsync(code);
if (delivery.StoragePath is not null)
    return Redirect(await r2.GetDownloadUrlAsync(delivery.StoragePath));
else
    return LocalDiskServe(delivery);
```

### Where they silently diverge

| Scenario | Dev A writes at Start time | What Dev A should do instead |
|----------|--------------------------|------------------------------|
| User starts upload, gets presigned URLs, never completes | `Delivery.StoragePath` is set but multipart upload is abandoned | Download handler tries to redirect to a URL for an object that doesn't exist yet → 403/404 from R2 |
| User starts upload for FILE_A, then starts another for FILE_B | Only one `Delivery.StoragePath` per delivery — second write overwrites first | Data loss |
| CommitR2UploadHandler also writes `StoragePath` (because Dev A left it out) | Two writers to the same column | Race condition or silent overwrite |

### Root cause
AD-7 says StoragePath stores the key. **It does not say who writes it, or when.** Two natural answers (start-time vs. complete-time) produce different states, and Dev A and Dev B don't need to coordinate — they each read the AD and pick the interpretation that fits their handler's flow. The result: either the download handler sees incomplete deliveries, or the commit handler doesn't set StoragePath and download always falls through to the null (local-disk) path.

---

## Divergence 4 — Error handling: completely ungoverned

### ADs obeyed
There is no AD about error handling, exception contracts, retry, or fault isolation. ADs 1-9 are silent on failure modes.

### What Dev A does (upload)
```csharp
public async Task<UploadInitResult> InitiateUploadAsync(...)
{
    // No retry — just let CreateMultipartUploadAsync throw
    var r2Upload = await s3Client.CreateMultipartUploadAsync(...);
}
```
```csharp
// StartR2UploadHandler.cs
try { result = await provider.InitiateUploadAsync(...); }
catch (AmazonS3Exception ex) { return Status(503, "R2 unavailable"); }
```

### What Dev B does (download)
```csharp
public async Task<Uri> GetDownloadUrlAsync(string objectKey)
{
    // Polly retry: 3 attempts with exponential backoff
    return await retryPolicy.ExecuteAsync(() =>
        s3Client.GetPresignedUrlAsync(...));
}
```

### Where they silently diverge

| Dimension | Dev A | Dev B | Impact |
|-----------|-------|-------|--------|
| **Retry philosophy** | No retry — fails fast | 3 retries with backoff | Inconsistent UX: upload fails immediately, download hangs 15s |
| **Exception types** | Catches `AmazonS3Exception` | Catches `AmazonS3Exception` AND `HttpRequestException` | Dev A's handler crashes on network timeout |
| **Fallback** | Returns 503 — upload fails | Throws upwards — no fallback | No consistent strategy |
| **Client-facing error shape** | Dev A returns structured `{error: "R2 unavailable"}` | Dev B returns `ProblemDetails` from FastEndpoints default | Inconsistent API responses |

### Root cause
**Zero ADs cover error handling.** No exception base type, no retry policy, no fallback, no client-facing error shape. The two handler developers will independently choose strategies that produce an inconsistent API surface and different runtime characteristics for upload vs. download.

---

## Divergence 5 — `IStorageProvider` method signatures are pseudocode, not a contract

### ADs obeyed
- AD-1: "IStorageProvider gains high-level R2 methods"
- AD-5: Four method signatures shown (with bare parameter names, no types)
- Deferred: "IStorageProvider exact method signatures — Emerges during implementation"

### What Dev A assumes
```csharp
Task<UploadInitResult> InitiateUploadAsync(string fileName, string contentType, long fileSize, Guid userId);
```

### What Dev B assumes
```csharp
Task<UploadInitResult> InitiateUploadAsync(string fileName, string contentType, long? fileSize, string userId);
```

Both are reasonable interpretations of the pseudocode. But if they're in the same interface, the compiler catches this **only if they coordinate before writing code**. The spine explicitly defers this to implementation time.

### Root cause
The Deferred section's "exact method signatures emerge during implementation" is directly contradictory to the purpose of an architecture spine. The spine is supposed to **prevent** divergence. Deferring the exact interface contract to implementation time guarantees that two implementors will guess differently.

---

## Summary of AD-level gaps

| Gap | ADs involved | Severity |
|-----|------------|----------|
| No shared `objectKey` builder — format described but not codified as API | AD-4, AD-7 | **HIGH** — 404 on download |
| `uploadId` type, lifetime, and persistence undefined | AD-2, AD-5 | **HIGH** — broken Commit flow |
| `Delivery.StoragePath` write-timing ambiguous | AD-7 | **MEDIUM** — dangling or null paths |
| Error handling entirely missing | None | **MEDIUM** — inconsistent API surface |
| Method signatures deferred to implementation | AD-1, AD-5, Deferred | **MEDIUM** — interface may not compile |
| `fileName` sanitization/encoding unspecified | AD-4 | **LOW** — fails on special chars only |

---

## Recommended AD additions

**AD-10 — Shared key builder (closes Divergence 1, 3)**
> `R2ObjectKeyBuilder.Build(userId, deliveryId, fileName)` is the sole producer and consumer of object keys. `StartR2UploadHandler` MUST call it. `DownloadDeliveryHandler` MUST use `Delivery.StoragePath` (written by the builder). Encoding: lowercase, no leading slash, URL-encoded fileName.

**AD-11 — uploadId contract (closes Divergence 2)**
> `uploadId` = the raw multipart upload ID returned by R2's `CreateMultipartUploadAsync`. Type: `string`. MUST be persisted to a new `Delivery.R2UploadId` column (nullable, cleared on complete). SERVERSIDE stateful — client only passes it through.
> Single-PUT path: `InitiateUploadAsync` returns `UploadInitResult` with `UploadId = null` and direct presigned URL.

**AD-12 — Error handling pattern (closes Divergence 4)**
> All R2 operations throw `R2StorageException` (wrapping `AmazonS3Exception`). Handlers catch `R2StorageException` and return `502 BadGateway` with `ProblemDetails`. Retry policy: 1 retry, 500ms delay, on throttle (429) and server (500) errors only.

---

## Verdict

**FLAG** — The spine has five active divergence seams. The two most critical are the missing shared object-key builder (guarantees a runtime 404 on download after the first deployment) and the undefined `uploadId` contract (breaks the multipart Commit flow). Both produce silent runtime failures with zero test-time detection in isolation.
