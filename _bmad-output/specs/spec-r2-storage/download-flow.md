# Download Flow (R2)

Replaces the existing local-disk download stream.

## Flow

```
Client                              Server                            Cloudflare R2
  │                                   │                                  │
  │ 1. GET /api/downloads/{code}/info─►│                                  │
  │                                   ├─ Lookup Delivery in PG ──────────│
  │                                   │                                  │
  │ ◄── {fileName, fileSize,          │                                  │
  │        mimeType, expiresAt,        │                                  │
  │        hasPin?}                    │                                  │
  │                                   │                                  │
  │ 2. GET /api/downloads/{code} ─────►│                                  │
  │                                   ├─ Generate presigned GET URL ─────│
  │                                   │  (valid for 60 seconds)          │
  │                                   │                                  │
  │ ◄── 302 Redirect ─────────────────┤                                  │
  │     Location: {signedUrl}         │                                  │
  │                                   │                                  │
  │ 3. Follow redirect ────────────────────────────────────────────────►│
  │                                   │                                  │
  │ 4. R2 serves file directly        │                                  │
```

## Key Changes

### DownloadInfoEndpoint
- Unchanged — still returns Delivery metadata from PG.

### DownloadEndpoint (Features/Downloads/Download/)
- **Before:** Reads file from local disk, streams to response.
- **After:** Generates presigned GET URL with 60-second expiry, returns `302 Redirect`. Client follows redirect to R2.

### Delivery Table
- `StoragePath` column (new) stores the R2 object key: `uploads/{deliveryId}/{fileName}`.
- Existing records with null StoragePath = local disk (backward compat). DownloadEndpoint checks: if StoragePath is set → R2 redirect; if null → local disk stream.

## Presigned URL Security
- 60-second TTL — short window prevents link sharing.
- URL tied to the specific object key — cannot be reused for other objects.
- HTTPS enforced by R2.
- `response-content-disposition` set to `attachment; filename="..."` so browser forces download.
