# Story 1.2: Chunked Upload API

- **Epic:** Epic 1 — Core Delivery Pipeline
- **Story ID:** 1.2
- **Status:** ready-for-dev
- **Dependencies:** Story 1.1 ✅ (Storage Backend Foundation — IStorageProvider, UploadSession, Delivery, Manifest, Chunk entities)

---

## User Story

> As a sender,
> I want to upload large files as chunks with resume capability,
> So that files of any size can be sent reliably over unstable connections.

---

## Requirements

### Business Value

This story implements the core upload pipeline. Without it, senders cannot transfer files to ZapDrop. It builds directly on Story 1.1's storage foundation — `IStorageProvider`, `UploadSession`, `Delivery`, `Manifest`, and `Chunk` entities — and adds the FastEndpoints API endpoints that clients use to initiate, perform, and finalize uploads.

### From the PRD

Binds FR-1 (split-and-store any-size file), FR-2 (chunked upload resume), and FR-3 (download reassembly). Supports future FR-4 (E2E encryption) and FR-5 (local key storage) by using the ciphertext-ready entity model.

### From the Architecture Spine

| AD | Rule | Implementation Impact |
|---|---|---|
| AD-2 | Three-phase upload: start → chunks → commit | Create 4 FastEndpoints following `/api/uploads/*` prefix |
| AD-3 | UploadSession 24h TTL with resume via GET status | `GET /api/uploads/{id}/status` returns uploaded/missing chunks |
| AD-4 | Idempotent chunk operations (skip-if-exists) | Chunk endpoint checks `uploadId + index` before storage |
| AD-5 | Wolverine tx: individual chunks auto-commit, final commit is atomic | Start/Chunk in individual tx, Commit in atomic block |
| AD-9 | `/api/uploads/*` prefix for upload operations | All endpoints under that prefix |
| AD-12 | LightResults `Result<T>` for error handling | Match existing handler patterns |
| AD-6 | UploadSession (1) → Chunks (many) for in-progress uploads | Query chunks by DeliveryId on commit |

---

## Technical Requirements

### 1. Endpoint: POST /api/uploads/start

Create `src/TeleCloud.Server/Features/Uploads/Start/StartUploadEndpoint.cs` + `StartUploadHandler.cs`

Request body:
```json
{
  "fileName": "video.mp4",
  "fileSize": 1073741824,
  "mimeType": "video/mp4"
}
```

Response (201):
```json
{
  "uploadId": "uuid-string",
  "chunkSize": 26214400
}
```

Handler logic:
1. Resolve `CurrentUserService.GetUserIdAsync()` for authenticated sender
2. Validate file size against plan limits — `POST /api/uploads/start` → 413 if Free plan exceeds 5GB
3. Create `UploadSession` with `Id = Guid.CreateVersion7()`, `SenderId`, `FileName`, `ContentType`, `FileSize`, `Status = "InProgress"`, `CreatedAt = DateTime.UtcNow`, `ExpiresAt = DateTime.UtcNow.AddHours(24)`
4. Save to DbContext
5. Return `UploadSession.Id` + default `chunkSize` (25MB = 25 * 1024 * 1024)

### 2. Endpoint: POST /api/uploads/{id}/chunks/{index}

Create `src/TeleCloud.Server/Features/Uploads/Chunks/UploadChunkEndpoint.cs` + `UploadChunkHandler.cs`

Request: Binary body (raw bytes via `IFormFile` or raw body)

Response (200):
```json
{ "chunkIndex": 0, "stored": true }
```
On retry:
```json
{ "chunkIndex": 0, "alreadyExists": true }
```

Handler logic:
1. Load `UploadSession` by `id`; if null → 404. If `Status != "InProgress"` → 410 expired. If `ExpiresAt < DateTime.UtcNow` → 410
2. Check if `Chunk` already exists for `(DeliveryId = uploadId, Index = chunkIndex)`; if yes → return `alreadyExists: true`
3. Read request body bytes
4. Call `IStorageProvider.StoreChunkAsync(chunkId, data, ct)` to persist
5. Create `Chunk` record: `Id = Guid.CreateVersion7()`, `DeliveryId = uploadSession.Id`, `Index`, `Size = data.Length`, `StorageRef = result from StoreChunkAsync`, `Sha256` (compute from byte data), `CreatedAt = DateTime.UtcNow`
6. Add to DbContext, save
7. Return stored success

Chunk size validation: if `data.Length` exceeds allocation — this should be enforced at the HTTP layer (max request body size).

### 3. Endpoint: GET /api/uploads/{id}/status

Create `src/TeleCloud.Server/Features/Uploads/Status/UploadStatusEndpoint.cs` + `UploadStatusHandler.cs`

Response (200):
```json
{
  "status": "InProgress",
  "totalChunks": 42,
  "uploadedChunks": [0, 1, 3],
  "missingChunks": [2, 4, 5, 6],
  "expiresAt": "2026-07-20T00:00:00Z"
}
```

Handler logic:
1. Load `UploadSession` by `id`; if null → 404. If expired → 410
2. Load all `Chunk` records for `DeliveryId == uploadSession.Id`, ordered by Index
3. Compute `uploadedChunks` (list of integer indices from Chunks) and `missingChunks` (indices from 0 to `totalChunks - 1` not in uploaded set)
4. `totalChunks` = ceil(`FileSize` / chunkSize) — but chunkSize isn't stored. Use the uploaded chunks + file size to derive, or just report what's uploaded

**Simplification:** Since chunkSize is a fixed constant (25MB), `totalChunks = (int)Math.Ceiling((double)uploadSession.FileSize / chunkSize)`. Report `uploadedChunks` as list of indices, `missingChunks` as complement.

### 4. Endpoint: POST /api/uploads/{id}/commit

Create `src/TeleCloud.Server/Features/Uploads/Commit/CommitUploadEndpoint.cs` + `CommitUploadHandler.cs`

Response (200):
```json
{
  "shareId": "abc123",
  "shareLink": "https://zapdrop.dev/d/abc123"
}
```

Handler logic (ATOMIC — use Wolverine transaction):
1. Load `UploadSession` by `id`; if null → 404. If not InProgress or expired → 410
2. Load all `Chunk` records for `DeliveryId == uploadSession.Id`
3. Validate: `chunks.Count` must equal `totalChunks`. If not → 400 with `{ "error": "incomplete_upload", "missingChunks": [...] }`
4. Generate `shareCode`: URL-safe base64 of random bytes (8 bytes → 11 chars)
5. Create `Delivery`: `Id = Guid.CreateVersion7()`, `SenderId = uploadSession.SenderId`, `FileName`, `ContentType`, `FileSize`, `TotalBytes = sum(chunks.Size)`, `ChunkCount`, `ShareCode`, `MaxDownloads` (plan-based), `CreatedAt = DateTime.UtcNow`
6. Create `Manifest`: `Id = Guid.CreateVersion7()`, `DeliveryId = delivery.Id`, `EncryptedData = []` (empty — will be populated in Story 1.4 E2E encryption), `CreatedAt = DateTime.UtcNow`
7. Link chunks to Delivery: update each `Chunk.DeliveryId` from `uploadSession.Id` to `delivery.Id`
8. Set `UploadSession.Status = "Committed"`
9. Save all changes in single `SaveChangesAsync` call (implicit Wolverine tx)
10. Return shareId + shareLink

### 5. Error Handling

Follow AD-12: use LightResults `Result<T>` pattern matching existing handlers:
- `NotFoundError` for missing upload sessions
- 410 status for expired sessions
- 400 for incomplete upload on commit
- 413 for file size exceeded (plan limits)

Use `AddError` / `ThrowError` in endpoints with `Send.OkAsync` / `Send.ErrorsAsync` for FastEndpoints pattern matching.

---

## Codebase Patterns to Follow

### Existing FastEndpoints Pattern (MUST MATCH)

Each endpoint follows the `Endpoint → Command → Handler` pattern:

**Endpoint** (e.g., `UploadFileEndpoint.cs`):
- Inherits `Endpoint<TRequest, TResponse>`
- `Configure()`: sets route, tags, summary
- `HandleAsync()`: resolves `IMessageBus`, invokes command, maps result

**Handler** (e.g., `UploadFileHandler.cs`):
- Public record `Command` with input parameters
- Handler class with `Handle(Command, TeleCloudDbContext, currentUser, ct)` method
- Returns `Result<TResponse>` via LightResults
- Uses `Guid.CreateVersion7()` for IDs

### File Location

All new files under `src/TeleCloud.Server/Features/Uploads/`:
```
Features/Uploads/
├── Start/
│   ├── StartUploadEndpoint.cs
│   └── StartUploadHandler.cs
├── Chunks/
│   ├── UploadChunkEndpoint.cs
│   └── UploadChunkHandler.cs
├── Status/
│   ├── UploadStatusEndpoint.cs
│   └── UploadStatusHandler.cs
└── Commit/
    ├── CommitUploadEndpoint.cs
    └── CommitUploadHandler.cs
```

### Shared Constants

Add chunk size constant somewhere accessible (e.g., a static class in `Features/Uploads/` or in `TeleCloudOptions`):

```csharp
public const int DefaultChunkSize = 25 * 1024 * 1024; // 25MB
```

This can be in a shared uploads constants file or injected via configuration.

---

## Acceptance Criteria

### AC-1: Start upload session
**Given** authenticated sender with valid Free plan  
**When** `POST /api/uploads/start` with `{ fileName: "video.mp4", fileSize: 500000000, mimeType: "video/mp4" }`  
**Then** returns 201 with `uploadId` (UUID) and `chunkSize: 26214400`

### AC-2: Plan limit enforcement
**Given** Free plan sender  
**When** `POST /api/uploads/start` with `fileSize > 5GB`  
**Then** returns 413 `{ maxSize: "5GB", currentPlan: "Free" }`

### AC-3: Upload single chunk
**Given** active upload session  
**When** `POST /api/uploads/{id}/chunks/0` with chunk bytes  
**Then** returns 200 `{ chunkIndex: 0, stored: true }`; chunk persisted via IStorageProvider

### AC-4: Idempotent chunk retry
**Given** chunk at index 0 already uploaded  
**When** `POST /api/uploads/{id}/chunks/0` with same bytes  
**Then** returns 200 `{ chunkIndex: 0, alreadyExists: true }` without duplicate storage

### AC-5: Upload status query
**Given** chunks at indices 0 and 1 uploaded, index 2 missing  
**When** `GET /api/uploads/{id}/status`  
**Then** returns 200 with `uploadedChunks: [0, 1]` and `missingChunks: [2, 3, ..., N-1]`

### AC-6: Commit with all chunks
**Given** all chunks uploaded  
**When** `POST /api/uploads/{id}/commit`  
**Then** returns 200 `{ shareId, shareLink }`; Delivery + Manifest created; UploadSession status = Committed

### AC-7: Commit with missing chunks
**Given** incomplete upload  
**When** `POST /api/uploads/{id}/commit`  
**Then** returns 400 `{ error: "incomplete_upload", missingChunks: [...] }`

### AC-8: Expired session
**Given** session with `ExpiresAt` in the past  
**When** any upload endpoint called  
**Then** returns 410 `{ error: "session_expired" }`

### AC-9: Non-existent session
**Given** invalid upload ID  
**When** any upload endpoint called  
**Then** returns 404

---

## Dev Notes

### Brownfield Considerations

- **Do NOT modify** existing `UploadSession`, `Chunk`, `Delivery`, `Manifest` entities — they were just created in Story 1.1 and are correct
- **Do NOT modify** `IStorageProvider` or `LocalDiskStorageProvider` — they work correctly
- **Do NOT touch** legacy `FileMetadata`, `TempShare`, `Folder` — still active
- **All new code** goes in `Features/Uploads/` — the existing `Features/Files/Upload/` is legacy Telegram flow
- The `chunkSize` constant (25MB) can be moved to configuration later. Hardcode for now.
- Plan limit checking: check `User.Plan` against `PlanTierLimits` for file size cap (Free=5GB, Pro=50GB). If the class doesn't expose a dedicated `MaxFileSize` for the upload flow, use existing tier constants.
- The `shareLink` URL uses `https://zapdrop.dev/d/{shareCode}` format. Note this is the final URL structure — the actual recipient page is Story 1.3.

### Testing Guidance

- Test each endpoint individually with focused handler tests
- Test idempotent chunk upload (same index twice → alreadyExists)
- Test commit with missing chunks (incomplete)
- Test plan limit enforcement (Free > 5GB)
- Test expired session (set ExpiresAt in past, then call endpoint)
- Test chunk SHA-256 computation consistency

### Common Pitfalls to Avoid

- ❌ Don't create chunks without validating the upload session is InProgress
- ❌ Don't forget to check `ct.ThrowIfCancellationRequested()` in long operations
- ❌ Don't link chunks to Delivery before commit — during upload they belong to UploadSession
- ❌ Don't create a Delivery without a Manifest
- ❌ Don't hardcode the share link domain — derive from configuration or `HttpContext.Request`
- ❌ Don't use `IMessageBus` in GET endpoints (status) — use direct DbContext queries
- ❌ Don't use `MemoryStream` for chunk body — read directly from request body or form file
