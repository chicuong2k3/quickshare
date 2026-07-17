# Server Changes — QuickShareCloud Backend

## New API Endpoints

### `POST /api/temp`

Anonymous file upload for temporary sharing. Returns a share link + expiry metadata.

**Request:** `multipart/form-data`
| Field | Type | Required | Description |
|---|---|---|---|
| `file` | file (binary) | Yes | The file to share |
| `expiry` | string | No | `"24h"` or `"7d"`. Default: `"24h"` |

**Response 201:**
```json
{
  "id": "uuid-v7",
  "link": "https://quicksharecloud.example/t/a1b2c3d4",
  "filename": "presentation.pdf",
  "size": 2457600,
  "expiresAt": "2026-07-18T14:30:00Z",
  "downloadUrl": "https://quicksharecloud.example/api/temp/a1b2c3d4/download"
}
```

**Response 400** — file missing, file too large (> 2 GB), or invalid expiry value.
**Response 413** — file exceeds size limit.

### `GET /api/temp/{shareId}`

Returns temp share metadata (used by the download page to show file info before download).

**Response 200:**
```json
{
  "id": "a1b2c3d4",
  "filename": "presentation.pdf",
  "size": 2457600,
  "contentType": "application/pdf",
  "expiresAt": "2026-07-18T14:30:00Z",
  "expired": false
}
```
**Response 404** — share not found or expired.

### `GET /api/temp/{shareId}/download`

Streams the file for download. Same semantics as the existing `GET /api/files/{id}/download` (range support, streaming).

**Response 200/206** — file stream.
**Response 410 Gone** — file expired.
**Response 404** — share not found.

## Data Model Addition

### `TempShare` table

| Column | Type | Description |
|---|---|---|
| `Id` | `uuid` (PK) | Share identifier (used in link path) |
| `FileId` | `uuid` (FK → files) | Reference to the uploaded file in Telegram |
| `ExpiresAt` | `timestamptz` | When this share becomes inaccessible |
| `CreatedAt` | `timestamptz` | When the share was created |

### `FileMetadata` additions
No changes to the existing `files` table. The `TempShare` table is a lightweight join table — files uploaded via `/api/temp` are standard `files` records with an associated `TempShare`.

## Cleanup

A `TempShareCleanupService` (IHostedService) runs on a timer (every 15 minutes):

1. Queries `TempShare` joined with `files` for entries where `ExpiresAt <= now()`.
2. For each expired share:
   - Deletes the file from Telegram via `TelegramService`.
   - Deletes the `files` record (cascades to `TempShare`).
3. Logs cleanup stats (count, total bytes reclaimed).

## API Key (Future / Non-goal)
Not in scope for this spec, but the architecture should not block adding API key auth to `/api/temp` later for rate limiting.
