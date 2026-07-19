# Story 1.5: Upload UI

- **Epic:** Epic 1 — Core Delivery Pipeline
- **Story ID:** 1.5
- **Status:** ready-for-dev
- **Dependencies:** Story 1.1 ✅, 1.2 ✅, 1.4 ✅

---

## Implementation

Update `src/TeleCloud.Web/Pages/Upload.razor` to use the chunked upload API from Story 1.2 instead of the legacy single-file Telegram upload.

### Key Changes
1. Replace single `POST /api/files` with chunked flow: start → chunk loop → commit
2. Add progress bar with percentage, speed, ETA
3. Show "Encrypting..." stage before upload (placeholder for Story 1.4 integration)
4. Show Share Link Card on completion with copy button
5. Preserve existing drop zone visual design
6. Add retry on chunk failure with resume from GET /api/uploads/{id}/status

### API Flow
```
POST /api/uploads/start → { uploadId, chunkSize }
POST /api/uploads/{id}/chunks/0, 1, 2, ... (with progress)
POST /api/uploads/{id}/commit → { shareId, shareLink }
```
