# QuickShare — Send File Architecture

## Overview

QuickShare supports **server-mediated file uploads** via two backends: a legacy chunked upload through the server (stored on local disk) and the primary **R2 direct-upload path** (client uploads to Cloudflare R2 via presigned URLs, server never sees file bytes). Files are encrypted optionally by the client (AES-256-GCM) and the chunk manifest is always encrypted server-side (AES-CBC, keyed by share code).

> Client → Server (R2 presigned URLs) → Client PUTs directly to R2 → Server (commit, create Delivery + Manifest) → Share link returned.

---

## 1. Component Map

### Server API Endpoints

| Endpoint | File | Role |
|----------|------|------|
| `POST /api/uploads/start-r2` | `Features/Uploads/StartR2/StartR2Endpoint.cs` | Initiate R2 multipart upload, return presigned part URLs |
| `POST /api/uploads/commit-r2` | `Features/Uploads/CommitR2/CommitR2Endpoint.cs` | Complete R2 upload, create Delivery record, return share link |
| `POST /api/uploads/abort-r2` | `Features/Uploads/AbortR2/AbortR2Endpoint.cs` | Cancel in-progress R2 upload |
| `POST /api/uploads/start` | `Features/Uploads/Start/StartUploadEndpoint.cs` | **Legacy** — start a server-chunked upload session |
| `POST /api/uploads/{id}/chunks/{index}` | `Features/Uploads/Chunks/UploadChunkEndpoint.cs` | **Legacy** — upload a single chunk to server |
| `GET /api/uploads/{id}/status` | `Features/Uploads/Status/UploadStatusEndpoint.cs` | **Legacy** — check chunk upload progress |
| `POST /api/uploads/{id}/commit` | `Features/Uploads/Commit/CommitUploadEndpoint.cs` | **Legacy** — commit chunks, create Delivery + Manifest |

### Server Business Logic

| Layer | File | Role |
|-------|------|------|
| StartR2 Handler | `Features/Uploads/StartR2/StartR2Handler.cs` | Validate plan limits, init R2 multipart, create UploadSession |
| CommitR2 Handler | `Features/Uploads/CommitR2/CommitR2Handler.cs` | Complete R2 multipart, create Delivery, generate share code |
| AbortR2 Handler | `Features/Uploads/AbortR2/AbortR2Handler.cs` | Abort R2 multipart, remove UploadSession |
| Start Handler | `Features/Uploads/Start/StartUploadHandler.cs` | **Legacy** — validate, create UploadSession |
| UploadChunk Handler | `Features/Uploads/Chunks/UploadChunkHandler.cs` | **Legacy** — store chunk, SHA-256 hash, idempotent retry |
| Commit Handler | `Features/Uploads/Commit/CommitUploadHandler.cs` | **Legacy** — validate all chunks present, create Delivery + encrypted Manifest |
| UploadStatus Handler | `Features/Uploads/Status/UploadStatusHandler.cs` | **Legacy** — enumerate uploaded/missing chunk indices |

### Storage Layer

| Layer | File | Role |
|-------|------|------|
| IStorageProvider | `Abstractions/IStorageProvider.cs` | Interface: chunk store/retrieve, R2 multipart lifecycle, presigned URLs |
| R2StorageProvider | `Infrastructure/Storage/R2StorageProvider.cs` | Cloudflare R2 via AWS S3 SDK (`IAmazonS3`) |
| LocalDiskStorageProvider | `Infrastructure/LocalDiskStorageProvider.cs` | **Legacy** — chunk files on local disk (`{chunkId:N}.chunk`) |

### Shared Models

| Model | File | Role |
|-------|------|------|
| `UploadSession` | `Shared/Models/UploadSession.cs` | Tracks in-progress upload: sender, file meta, status, R2 IDs |
| `Delivery` | `Shared/Models/Delivery.cs` | The "share" — file metadata, share code, download limits, branding |
| `Chunk` | `Shared/Models/Chunk.cs` | One chunk of a file: index, size, storage ref, SHA-256 |
| `Manifest` | `Shared/Models/Manifest.cs` | Encrypted chunk index (binary blob, AES-CBC keyed by share code) |
| `ManifestData` | `Features/Uploads/Shared/ManifestData.cs` | Plaintext manifest: file metadata + `ChunkEntry[]` |
| `ManifestEncryption` | `Features/Uploads/Shared/ManifestEncryption.cs` | AES-CBC encrypt/decrypt manifest using SHA-256 of share code |
| `UploadConstants` | `Features/Uploads/UploadConstants.cs` | `DefaultChunkSize = 25 MB` |
| `PlanTierLimits` | `Shared/Constants/PlanTierLimits.cs` | Per-plan limits: max file size, max expiry, max downloads, concurrency |

### Frontend

| Layer | File | Role |
|-------|------|------|
| Upload Page | `Pages/Upload.razor` | Route `/` — full send page: drag/drop, paste, encryption toggle, expiry picker |
| Upload Logic | `Pages/Upload.razor.js` | State machine: idle → uploading → success/error, R2 multipart orchestration |

---

## 2. Upload Paths

### 2.1 R2 Direct Upload (Primary — Active)

```
Client Browser              Server API                    Cloudflare R2
  │                           │                              │
  │  POST /api/uploads/       │                              │
  │    start-r2               │                              │
  │  { fileName, fileSize,    │                              │
  │    contentType,           │                              │
  │    partNumbers }          │                              │
  │──────────────────────────▶│                              │
  │                           │── Validate plan limits       │
  │                           │── InitR2MultipartUploadAsync │
  │                           │──────────────────────────────▶│
  │                           │◀── { UploadId, ObjectKey }   │
  │                           │                              │
  │                           │── GenerateUploadPartUrlsAsync│
  │                           │──────────────────────────────▶│
  │                           │◀── [R2PartUrl × N]           │
  │                           │                              │
  │                           │── Create UploadSession       │
  │                           │   (Status="InProgress")      │
  │                           │                              │
  │  { deliveryId, uploadId,  │                              │
  │    objectKey,             │                              │
  │    parts: [{partNumber,   │                              │
  │             url}] }       │                              │
  │◀──────────────────────────│                              │
  │                           │                              │
  │  === Upload parts directly to R2 (4 concurrent) ===      │
  │                           │                              │
  │  PUT {partInfo.url}       │                              │
  │  (file slice as body)     │                              │
  │──────────────────────────────────────────────────────────▶│
  │◀── ETag ─────────────────────────────────────────────────│
  │  ... repeat for each part ...                            │
  │                           │                              │
  │  === All parts done ===   │                              │
  │                           │                              │
  │  POST /api/uploads/       │                              │
  │    commit-r2              │                              │
  │  { deliveryId, uploadId,  │                              │
  │    objectKey, fileName,   │                              │
  │    contentType, fileSize, │                              │
  │    parts: [{partNumber,   │                              │
  │             etag}] }      │                              │
  │──────────────────────────▶│                              │
  │                           │── CompleteR2MultipartUpload  │
  │                           │──────────────────────────────▶│
  │                           │◀── Success                   │
  │                           │                              │
  │                           │── Generate share code        │
  │                           │   (8 bytes → base64url ~11c) │
  │                           │── Create Delivery record     │
  │                           │   (StoragePath = objectKey)  │
  │                           │── Mark UploadSession Committed│
  │                           │                              │
  │  { shareCode, shareLink } │                              │
  │◀──────────────────────────│                              │
  │                           │                              │
  │  shareLink = /d/{code}    │                              │
```

**Key differences from legacy path:**
- Client uploads directly to R2 (server never touches file bytes)
- No `Chunk` or `Manifest` DB records — single R2 object per file
- `Delivery.StoragePath` holds the R2 object key
- Download uses `GetPresignedDownloadUrlAsync` instead of reassembling chunks

### 2.2 Legacy Chunked Upload (Server-Mediated, Deprecated)

```
Client              Server API                  DB / Disk
  │                     │                          │
  │  POST /api/uploads/ │                          │
  │    start            │                          │
  │  { fileName,        │                          │
  │    fileSize,        │                          │
  │    mimeType }       │                          │
  │────────────────────▶│                          │
  │                     │── Validate plan limits   │
  │                     │── Create UploadSession   │
  │                     │   (Status="InProgress")  │
  │  { uploadId,        │                          │
  │    chunkSize }      │                          │
  │◀────────────────────│                          │
  │                     │                          │
  │  === Upload each chunk ===                    │
  │                     │                          │
  │  POST /api/uploads/ │                          │
  │    {id}/chunks/{i}  │                          │
  │  (raw bytes body)   │                          │
  │────────────────────▶│                          │
  │                     │── Validate session       │
  │                     │── Check idempotency      │
  │                     │── StoreChunkAsync → disk │── chunk file
  │                     │── SHA-256 hash chunk     │
  │                     │── Insert Chunk record ───▶│── chunks table
  │  { chunkIndex,      │                          │
  │    stored: true }   │                          │
  │◀────────────────────│                          │
  │  ... repeat ...     │                          │
  │                     │                          │
  │  === All chunks done ===                      │
  │                     │                          │
  │  POST /api/uploads/ │                          │
  │    {id}/commit      │                          │
  │────────────────────▶│                          │
  │                     │── Verify all chunks      │
  │                     │   present (by index)     │
  │                     │── Generate share code    │
  │                     │── Create Delivery        │
  │                     │── Create encrypted       │
  │                     │   Manifest (ManifestData │
  │                     │   → AES-CBC)             │
  │                     │── Reassign chunks        │
  │                     │   DeliveryId             │
  │                     │── Mark session Committed │
  │  { shareId,         │                          │
  │    shareLink }      │                          │
  │◀────────────────────│                          │
```

**Download path (legacy):**
- `GET /d/{shareCode}` → decrypt Manifest → reassemble chunks from storage → stream to client

---

## 3. Entity Relationships

```
UploadSession                         Delivery                         Chunk
┌─────────────────┐                  ┌──────────────────┐             ┌──────────────┐
│ Id (Guid PK)    │                  │ Id (Guid PK)     │             │ Id (Guid PK) │
│ SenderId (FK)   │──► User          │ SenderId (FK)    │──► User     │ DeliveryId   │──► Delivery
│ FileName        │                  │ FileName         │             │ Index (int)  │
│ ContentType     │    commit ──►    │ ContentType      │             │ Size         │
│ FileSize        │                  │ FileSize         │             │ StorageRef   │
│ Status          │                  │ TotalBytes       │             │ Sha256       │
│ ExpiresAt       │                  │ ChunkCount       │             │ CreatedAt    │
│ R2UploadId?     │                  │ ShareCode (uniq) │             └──────────────┘
│ StoragePath?    │                  │ MaxDownloads     │
└─────────────────┘                  │ DownloadCount    │
                                     │ EgressBytes      │          Manifest
    Status values:                   │ ExpiresAt?       │          ┌──────────────────┐
    "InProgress"                     │ PasswordHash?    │          │ Id (Guid PK)     │
    "Committed"                      │ BrandLogo?       │          │ DeliveryId (FK)  │──► Delivery
    (expired = deleted)              │ BrandMessage?    │          │ EncryptedData    │
                                     │ BrandAccent?     │          │ (bytea = AES-CBC │
                                     │ Price?           │          │  ManifestData    │
                                     │ PaymentStatus?   │          │  JSON)           │
                                     │ Status           │          │ CreatedAt        │
                                     │ StoragePath?     │          └──────────────────┘
                                     └──────────────────┘

ManifestData (plaintext, serialized to JSON before encryption):
┌──────────────────────────────────────┐
│ FileName, ContentType, FileSize      │
│ TotalBytes, ChunkCount               │
│ Chunks: ChunkEntry[]                 │
│   [{ Index, StorageRef, Sha256, Size }]│
└──────────────────────────────────────┘
```

**Note:** R2 path does NOT use `Chunk` or `Manifest` tables. The entire file is one R2 object; `Delivery.StoragePath` holds the R2 object key. The legacy path uses chunks + manifest.

---

## 4. Manifest Encryption

The chunk manifest is encrypted server-side so that the chunk layout is only available to someone who knows the share code.

```
ManifestEncryption.Encrypt(manifestData, shareCode):
  key = SHA256(UTF8(shareCode))         // 32 bytes
  aes.IV = 16 zero bytes                // unique per share code, safe
  ciphertext = AES-CBC-Encrypt(JSON(manifestData), key, IV)
  → stored in Manifest.EncryptedData

ManifestEncryption.Decrypt(encryptedData, shareCode):
  key = SHA256(UTF8(shareCode))
  plaintext = AES-CBC-Decrypt(encryptedData, key, IV)
  → ManifestData from JSON
```

---

## 5. Client-Side Encryption (Optional)

The upload page offers an **"Encrypt"** toggle. When enabled, the client encrypts the entire file before upload.

```
Encryption Flow:
  1. User toggles "Encrypt" ON
  2. Client generates AES-256-GCM key via Web Crypto
  3. Client encrypts entire file: AES-256-GCM(file, key, 12-byte random nonce)
  4. Encrypted blob = nonce || ciphertext (prepended to results)
  5. Upload proceeds normally (R2 or legacy)
  6. After commit, the raw key is appended to URL as fragment:
     /d/{shareCode}#key={base64url(rawKey)}
  → Server NEVER sees the key. Only someone with the full URL can decrypt.
  → Key is in the URL fragment (not sent to server during download page load).
```

---

## 6. Plan Limits Enforcement

Checked in `StartUploadHandler` and `StartR2Handler`:

| Limit | Free | Pro | Studio |
|-------|------|-----|--------|
| Max file size | 100 MB | 2 GB | 20 GB |
| Max expiry days | 7 | 30 | 90 |
| Max downloads | 5 | 50 | unlimited |
| Max monthly bytes | 2 GB | 50 GB | 500 GB |
| Max concurrent (legacy) | 2 | 4 | 6 |

---

## 7. Upload Page State Machine

The upload page at `/` renders one section at a time:

| State | Section ID | Trigger |
|-------|-----------|---------|
| **idle** | `upload-idle` | Page load — drag/drop zone, paste hint, encrypt toggle, expiry picker |
| **dragging** | `upload-idle` (visual) | File being dragged over drop zone |
| **uploading** | `upload-uploading` | File selected — progress bar, speed, ETA, cancel button |
| **success** | `upload-success` | Commit returns share link — copy button, QR code, encrypt badge |
| **error** | `upload-error` | Any failure — error message, retry button |

---

## 8. Upload Orchestration (Upload.razor.js)

```
doUpload(file):
  1. Optional encryption:
     - webcrypto.generateKey(AES-GCM, 256)
     - encrypt(file, key) → nonce + ciphertext
     - encode raw key → base64url

  2. Split into parts:
     - effectiveSize = encrypted blob (or original if no encryption)
     - totalParts = ceil(effectiveSize / 25MB)
     - up to 5 GB total (MAX_FILE_BYTES)

  3. POST /api/uploads/start-r2
     → session = { deliveryId, uploadId, objectKey, parts[] }

  4. Upload parts:
     - 4 concurrent parts (CONCURRENT_PARTS)
     - Each: PUT presigned URL with file.slice(start, end)
     - Collect ETag per part
     - Update progress, speed, ETA

  5. POST /api/uploads/commit-r2
     → result = { shareCode, shareLink }

  6. Success:
     - If encrypted: append #key={encryptionKeyStr} to URL
     - Render QR code
     - Auto-copy link to clipboard
```

---

## 9. Cancel / Abort

```
Client cancels mid-upload:
  → AbortController.abort() stops in-flight fetch requests
  → POST /api/uploads/abort-r2 { deliveryId, uploadId, objectKey }
  → Server: storage.AbortR2MultipartUploadAsync()
  → Server: Remove UploadSession from DB
```

---

## 10. Server Wiring

In `src/QuickShare/Program.cs`:

```csharp
// Storage provider (configured via env/storage options)
builder.Services.AddScoped<IStorageProvider, R2StorageProvider>();
// Or for local dev:
builder.Services.AddScoped<IStorageProvider, LocalDiskStorageProvider>();
```

All upload endpoints use FastEndpoints with the standard cookie auth (no explicit `AllowAnonymous()` — they require authentication).

---

## 11. File Reference

| File | Role |
|------|------|
| `src/QuickShare/Pages/Upload.razor` | Send page UI (`/` route) — drag/drop, paste, encrypt, expiry |
| `src/QuickShare/Pages/Upload.razor.js` | Upload state machine + R2 multipart orchestration |
| `src/QuickShare/Features/Uploads/StartR2/StartR2Endpoint.cs` | `POST /api/uploads/start-r2` |
| `src/QuickShare/Features/Uploads/StartR2/StartR2Handler.cs` | R2 init + UploadSession creation |
| `src/QuickShare/Features/Uploads/CommitR2/CommitR2Endpoint.cs` | `POST /api/uploads/commit-r2` |
| `src/QuickShare/Features/Uploads/CommitR2/CommitR2Handler.cs` | R2 completion + Delivery creation |
| `src/QuickShare/Features/Uploads/AbortR2/AbortR2Endpoint.cs` | `POST /api/uploads/abort-r2` |
| `src/QuickShare/Features/Uploads/AbortR2/AbortR2Handler.cs` | R2 abort + session cleanup |
| `src/QuickShare/Features/Uploads/Start/StartUploadEndpoint.cs` | **Legacy** `POST /api/uploads/start` |
| `src/QuickShare/Features/Uploads/Start/StartUploadHandler.cs` | **Legacy** session creation |
| `src/QuickShare/Features/Uploads/Chunks/UploadChunkEndpoint.cs` | **Legacy** `POST /api/uploads/{id}/chunks/{index}` |
| `src/QuickShare/Features/Uploads/Chunks/UploadChunkHandler.cs` | **Legacy** chunk storage + SHA-256 |
| `src/QuickShare/Features/Uploads/Commit/CommitUploadEndpoint.cs` | **Legacy** `POST /api/uploads/{id}/commit` |
| `src/QuickShare/Features/Uploads/Commit/CommitUploadHandler.cs` | **Legacy** chunk validation → Delivery + Manifest |
| `src/QuickShare/Features/Uploads/Status/UploadStatusEndpoint.cs` | **Legacy** `GET /api/uploads/{id}/status` |
| `src/QuickShare/Features/Uploads/Status/UploadStatusHandler.cs` | **Legacy** chunk progress query |
| `src/QuickShare/Features/Uploads/Shared/ManifestData.cs` | Manifest plaintext record (+ ChunkEntry) |
| `src/QuickShare/Features/Uploads/Shared/ManifestEncryption.cs` | AES-CBC manifest encrypt/decrypt |
| `src/QuickShare/Features/Uploads/UploadConstants.cs` | DefaultChunkSize = 25 MB |
| `src/QuickShare/Abstractions/IStorageProvider.cs` | Storage interface |
| `src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs` | Cloudflare R2 via AWS SDK |
| `src/QuickShare/Infrastructure/LocalDiskStorageProvider.cs` | **Legacy** local disk storage |
| `src/QuickShare.Shared/Models/UploadSession.cs` | Upload session entity |
| `src/QuickShare.Shared/Models/Delivery.cs` | Delivery (share) entity |
| `src/QuickShare.Shared/Models/Chunk.cs` | Chunk entity |
| `src/QuickShare.Shared/Models/Manifest.cs` | Manifest entity |
| `src/QuickShare.Shared/Constants/PlanTierLimits.cs` | Per-plan limits |
