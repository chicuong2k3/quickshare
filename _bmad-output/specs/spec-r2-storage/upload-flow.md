# Upload Flow (R2)

Replaces the existing local-disk chunked upload flow.

## Flow

```
Client                              Server                            Cloudflare R2
  │                                   │                                  │
  │ 1. POST /api/uploads/start ──────►│                                  │
  │     {fileName, fileSize, mime}    │                                  │
  │                                   │                                   │
  │                                   ├─ 1a. Init multipart upload ─────►│
  │                                   │   CreateMultipartUploadAsync()   │
  │                                   │  ◄── UploadId ──────────────────│
  │                                   │                                  │
  │                                   ├─ 1b. Generate presigned part URLs│
  │                                   │   for each chunk (max 25MB each) │
  │                                   │                                  │
  │ ◄── {uploadId, parts:[{partNumber,│                                  │
  │       signedUrl}], uploadId}       │                                  │
  │                                   │                                  │
  │ 2. PUT each chunk directly ──────────────────────────────────────────►│
  │    (parallel, up to 4 concurrent) │                                  │
  │    Response: ETag per part        │                                  │
  │                                   │                                  │
  │ 3. POST /api/uploads/commit ─────►│                                  │
  │     {uploadId, parts:[{partNumber,│                                  │
  │       etag}]}                     │                                  │
  │                                   ├─ 3a. CompleteMultipartUpload ───►│
  │                                   │  ◄── Success ───────────────────│
  │                                   │                                  │
  │                                   ├─ 3b. Create Delivery record (PG) │
  │                                   │                                  │
  │ ◄── {shareLink, shareCode} ───────┤                                  │
```

## Key Changes to Existing Endpoints

### StartUploadEndpoint
- **Before:** Returns `{uploadId, chunkSize}`, creates UploadSession in DB.
- **After:** Returns `{uploadId, uploadId (R2), parts: [{partNumber, signedUrl, etag?}]}`. Initializes S3 multipart upload. Server never stores file bytes.

### ChunksEndpoint (Features/Uploads/Chunks/)
- **Before:** Receives chunk bytes, writes to disk, stores to DB.
- **After:** **Removed entirely.** Client PUTs chunks directly to R2 signed URLs. Server has no chunk receive endpoint.

### CommitEndpoint
- **Before:** Counts stored chunks on disk, assembles file, creates Delivery.
- **After:** Calls `CompleteMultipartUploadAsync` on R2, then creates Delivery. If R2 returns error, returns failure without Delivery creation.

### UploadSession Table
- Still used for tracking, but no longer stores chunk metadata against each session. R2 holds the parts.

## Small Files (<10MB / single chunk)
- `StartUpload` detects small file → returns single presigned PUT URL (not multipart)
- `Commit` calls `PutObjectAsync` + `HeadObjectAsync` to verify instead of multipart complete
- Threshold: 10MB (configurable in `StorageOptions` as `R2MultipartThreshold`)

## Encryption at Rest
- Server-side encryption with S3-managed key (SSE-S3) via R2 — zero config, zero performance cost.
- Client-side encryption (PIN) remains an independent layer on top (per AD-5).
