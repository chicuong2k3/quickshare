# QuickShare — Send File Architecture

## Overview

QuickShare supports **server-mediated file uploads** via the **R2 direct-upload path** (client uploads to Cloudflare R2 via presigned URLs, server never sees file bytes). Files are encrypted optionally by the client (AES-256-GCM) and the chunk manifest is always encrypted server-side (AES-CBC, keyed by share code).

> Client → Server (R2 presigned URLs) → Client PUTs directly to R2 → Server (commit, create Delivery + Manifest) → Share link returned.

---

## 1. Component Map

### Server API Endpoints

| Endpoint | File | Role |
|----------|------|------|
| `POST /api/uploads/start-r2` | `Features/Uploads/StartR2/StartR2Endpoint.cs` | Initiate R2 multipart upload, return presigned part URLs |
| `POST /api/uploads/commit-r2` | `Features/Uploads/CommitR2/CommitR2Endpoint.cs` | Complete R2 upload, create Delivery record, return share link |
| `POST /api/uploads/abort-r2` | `Features/Uploads/AbortR2/AbortR2Endpoint.cs` | Cancel in-progress R2 upload |
| `GET /confirm-sender-email/{token}` | `Features/Notifications/SenderConfirm/SenderConfirmEmailEndpoint.cs` | Mark anonymous sender email confirmed (single-use DataProtection token, 24h expiry) |
| `GET /unsubscribe-sender/{token}` | `Features/Notifications/SenderUnsubscribe/SenderUnsubscribeEndpoint.cs` | Opt out of sender link emails for one delivery (`SenderEmailConsent = false`) |

### Server Business Logic

| Layer | File | Role |
|-------|------|------|
| StartR2 Handler | `Features/Uploads/StartR2/StartR2Handler.cs` | Validate plan limits, init R2 multipart, create UploadSession |
| CommitR2 Handler | `Features/Uploads/CommitR2/CommitR2Handler.cs` | Complete R2 multipart, create Delivery, generate share code |
| AbortR2 Handler | `Features/Uploads/AbortR2/AbortR2Handler.cs` | Abort R2 multipart, remove UploadSession |

### Storage Layer

| Layer | File | Role |
|-------|------|------|
| IStorageProvider | `Infrastructure/IStorageProvider.cs` | Interface: chunk store/retrieve, R2 multipart lifecycle, presigned URLs |
| R2StorageProvider | `Infrastructure/Storage/R2StorageProvider.cs` | Cloudflare R2 via AWS S3 SDK (`IAmazonS3`) |
| LocalDiskStorageProvider | `Infrastructure/LocalDiskStorageProvider.cs` | **Deprecated** — local disk storage (R2 is primary) |

### Shared Models

| Model | File | Role |
|-------|------|------|
| `UploadSession` | `Data/Entities/UploadSession.cs` | Tracks in-progress upload: sender, optional `SenderEmail`, file meta, status, R2 IDs |
| `Delivery` | `Data/Entities/Delivery.cs` | The "share" — file metadata, share code, download limits, branding, `SenderEmail`/`SenderEmailConfirmed`/`SenderEmailConsent` (issue #76) |
| `Chunk` | `Data/Entities/Chunk.cs` | One chunk of a file: index, size, storage ref, SHA-256 |
| `Manifest` | `Data/Entities/Manifest.cs` | Encrypted chunk index (binary blob, AES-CBC keyed by share code) |
| `ManifestData` | `Features/Uploads/Shared/ManifestData.cs` | Plaintext manifest: file metadata + `ChunkEntry[]` |
| `ManifestEncryption` | `Features/Uploads/Shared/ManifestEncryption.cs` | AES-CBC encrypt/decrypt manifest using SHA-256 of share code |
| `UploadConstants` | `Features/Uploads/UploadConstants.cs` | `DefaultChunkSize = 25 MB` |
| `PlanTierLimits` | `Infrastructure/PlanTierLimits.cs` | Per-plan limits: max file size, max expiry, max downloads, concurrency |

### Frontend

| Layer | File | Role |
|-------|------|------|
| Upload Page | `Pages/Upload.razor` | Route `/` — full send page: drag/drop, paste, encryption toggle, expiry picker |
| Upload Logic | `wwwroot/js/upload.js` | State machine: idle → uploading → success/error, R2 multipart orchestration |

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
  │    partNumbers,           │                              │
  │    senderEmail? }         │   ← anonymous opt-in (issue #76)│
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
  │                           │   (SenderEmail ← session)    │
  │                           │   (SenderEmailConsent=true   │
  │                           │    when email present)       │
  │                           │── Mark UploadSession Committed│
  │                           │                              │
  │                           │── (if SenderEmail + SMTP     │
  │                           │    configured)               │
  │                           │   Send link email:           │
  │                           │   share link + confirm token │
  │                           │   + unsubscribe token        │
  │                           │   (best-effort, rate-limited │
  │                           │    5/email/h, 20/IP/h —      │
  │                           │    failure never fails       │
  │                           │    the commit)               │
  │                           │                              │
  │  { shareCode, shareLink } │                              │
  │◀──────────────────────────│                              │
  │                           │                              │
  │  shareLink = /d/{code}    │                              │
```

**Key differences from legacy path (removed):**
- Client uploads directly to R2 (server never touches file bytes)
- No `Chunk` or `Manifest` DB records — single R2 object per file
- `Delivery.StoragePath` holds the R2 object key
- Download uses `GetPresignedDownloadUrlAsync` instead of reassembling chunks

---

## 3. Entity Relationships

```
UploadSession                         Delivery                         Chunk
┌─────────────────┐                  ┌──────────────────┐             ┌──────────────┐
│ Id (Guid PK)    │                  │ Id (Guid PK)     │             │ Id (Guid PK) │
│ SenderId (FK)   │──► User          │ SenderId (FK)    │──► User     │ DeliveryId   │──► Delivery
│ SenderEmail?    │                  │ SenderEmail?     │             │ Index (int)  │
│ FileName        │                  │ SenderEmail-     │             │ Size         │
│ ContentType     │    commit ──►    │   Confirmed      │             │ StorageRef   │
│ FileSize        │                  │ SenderEmail-     │             │ Sha256       │
│ Status          │                  │   Consent        │             │ CreatedAt    │
│ ExpiresAt       │                  │ FileName         │             └──────────────┘
│ R2UploadId?     │                  │ ContentType      │
│ StoragePath?    │                  │ FileSize         │
└─────────────────┘                  │ TotalBytes       │
                                     │ ChunkCount       │
    Status values:                   │ ShareCode (uniq) │
    "InProgress"                     │ MaxDownloads     │
    "Committed"                      │ DownloadCount    │
    (expired = deleted)              │ EgressBytes      │          Manifest
                                     │ ExpiresAt?       │          ┌──────────────────┐
                                     │ PasswordHash?    │          │ Id (Guid PK)     │
                                     │ BrandLogo?       │          │ DeliveryId (FK)  │──► Delivery
                                     │ BrandMessage?    │          │ EncryptedData    │
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

**Note:** R2 path does NOT use `Chunk` or `Manifest` tables. The entire file is one R2 object; `Delivery.StoragePath` holds the R2 object key.

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

## 5. Client-Side Encryption (Always-On)

Every upload is encrypted client-side **before the bytes leave the browser** — there is no toggle (`upload.js` sets `encryptionEnabled: true` unconditionally).

```
Encryption Flow (every upload):
  1. Client generates AES-256-GCM key via Web Crypto
  2. Client encrypts entire file: AES-256-GCM(file, key, 12-byte random nonce)
  3. Encrypted blob = nonce || ciphertext (prepended to results)
  4. Upload proceeds normally
  5. After commit, the raw key is appended to URL as fragment:
     /d/{shareCode}#key={base64url(rawKey)}
  → Server NEVER sees the key. Only someone with the full URL can decrypt.
  → Key is in the URL fragment (not sent to server during download page load).
```

---

## 6. Plan Limits Enforcement

Checked in `StartR2Handler`:

| Limit | Free | Pro | Studio |
|-------|------|-----|--------|
| Max file size | 100 MB | 2 GB | 20 GB |
| Max expiry days | 7 | 30 | 90 |
| Max downloads | 5 | 50 | unlimited |
| Max monthly bytes | 2 GB | 50 GB | 500 GB |
| Max concurrent | 2 | 4 | 6 |

---

## 7. Upload Page State Machine

The upload page at `/` renders one section at a time:

| State | Section ID | Trigger |
|-------|-----------|---------|
| **idle** | `upload-idle` | Page load — drag/drop zone, paste hint, always-on encryption badge, expiry picker, optional sender-email input (anonymous only, issue #76) |
| **dragging** | `upload-idle` (visual) | File being dragged over drop zone |
| **uploading** | `upload-uploading` | File selected — progress bar, speed, ETA, cancel button |
| **success** | `upload-success` | Commit returns share link — copy button, QR code, encrypt badge |
| **error** | `upload-error` | Any failure — error message, retry button |

---

## 8. Upload Orchestration (wwwroot/js/upload.js)

```
doUpload(file):
  1. Always-on encryption:
     - webcrypto.generateKey(AES-GCM, 256)
     - encrypt(file, key) → nonce + ciphertext
     - encode raw key → base64url

  2. Split into parts:
     - effectiveSize = encrypted blob (or original if no encryption)
     - totalParts = ceil(effectiveSize / 25MB)
     - up to 5 GB total (MAX_FILE_BYTES)

  3. Read optional sender email (#upload-sender-email, anonymous only)

  4. POST /api/uploads/start-r2
     → session = { deliveryId, uploadId, objectKey, parts[] }
     body includes senderEmail (or null)

  5. Upload parts:
     - 4 concurrent parts (CONCURRENT_PARTS)
     - Each: PUT presigned URL with file.slice(start, end)
     - Collect ETag per part
     - Update progress, speed, ETA

  6. POST /api/uploads/commit-r2
     → result = { shareCode, shareLink }

  7. Success:
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
builder.Services.AddSingleton<R2StorageProvider>();
// IStorageProvider resolves to R2 by default.
builder.Services.AddSingleton<IStorageProvider>(sp =>
    sp.GetRequiredService<R2StorageProvider>());
// When R2 is NOT configured, fall back to local disk:
builder.Services.AddSingleton<IStorageProvider, LocalDiskStorageProvider>();
```

All upload endpoints use FastEndpoints with the standard cookie auth (no explicit `AllowAnonymous()` — they require authentication).

---

## 11. File Reference

| File | Role |
|------|------|
| `src/QuickShare/Pages/Upload.razor` | Send page UI (`/` route) — drag/drop, paste, encrypt, expiry |
| `src/QuickShare/wwwroot/js/upload.js` | Upload state machine + R2 multipart orchestration |
| `src/QuickShare/Features/Uploads/StartR2/StartR2Endpoint.cs` | `POST /api/uploads/start-r2` |
| `src/QuickShare/Features/Uploads/StartR2/StartR2Handler.cs` | R2 init + UploadSession creation |
| `src/QuickShare/Features/Uploads/CommitR2/CommitR2Endpoint.cs` | `POST /api/uploads/commit-r2` |
| `src/QuickShare/Features/Uploads/CommitR2/CommitR2Handler.cs` | R2 completion + Delivery creation |
| `src/QuickShare/Features/Uploads/AbortR2/AbortR2Endpoint.cs` | `POST /api/uploads/abort-r2` |
| `src/QuickShare/Features/Uploads/AbortR2/AbortR2Handler.cs` | R2 abort + session cleanup |
| `src/QuickShare/Features/Uploads/UploadConstants.cs` | DefaultChunkSize = 25 MB |
| `src/QuickShare/Infrastructure/IStorageProvider.cs` | Storage interface |
| `src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs` | Cloudflare R2 via AWS SDK |
| `src/QuickShare/Infrastructure/LocalDiskStorageProvider.cs` | **Deprecated** local disk storage |
| `src/QuickShare/Data/Entities/UploadSession.cs` | Upload session entity |
| `src/QuickShare/Data/Entities/Delivery.cs` | Delivery (share) entity |
| `src/QuickShare/Data/Entities/Chunk.cs` | Chunk entity (deprecated — R2 handles chunking) |
| `src/QuickShare/Data/Entities/Manifest.cs` | Manifest entity (deprecated — R2 is single object) |
| `src/QuickShare/Infrastructure/PlanTierLimits.cs` | Per-plan limits |
