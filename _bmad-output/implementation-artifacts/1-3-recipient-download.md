# Story 1.3: Recipient Download

- **Epic:** Epic 1 — Core Delivery Pipeline
- **Story ID:** 1.3
- **Status:** ready-for-dev
- **Dependencies:** Story 1.1 ✅, Story 1.2 ✅

---

## User Story

> As a recipient,
> I want to open a link and download without creating an account,
> So that receiving files is frictionless.

---

## Acceptance Criteria

| AC | Description |
|---|---|
| AC-1 | `GET /d/{shareId}` returns download page: file name, size, download button; no auth |
| AC-2 | Download streams chunks, SHA-256 verified per-chunk; browser receives complete file |
| AC-3 | Chunk SHA-256 mismatch → re-fetched (not whole file); 3 failures → error |
| AC-4 | Expired delivery → 410 page "This delivery has expired" |
| AC-5 | Invalid shareId → 404 "This link doesn't exist" |
| AC-6 | Download-cap reached → button disabled "Download limit reached" |
| AC-7 | Successful download → counter incremented; IP + timestamp logged; EgressBytes updated |

---

## Technical Requirements

### 1. Public Download Page (Blazor SSR)

Update `src/TeleCloud.Web/Pages/Download.razor` to show:
- File name, file size
- Download button
- If expired → "This delivery has expired"
- If 404 → "This link doesn't exist"
- If download limit reached → disabled button with message
- No authentication required

Route: `@page "/d/{ShareCode}"`

### 2. Download API Endpoint

Create `src/TeleCloud.Server/Features/Downloads/Download/DownloadDeliveryEndpoint.cs`:
- `GET /api/downloads/{ShareCode}` — returns file as `application/octet-stream`
- Checks Delivery exists by ShareCode
- Checks not expired (ExpiresAt)
- Checks download count < MaxDownloads
- Loads chunks ordered by Index
- Streams each chunk from IStorageProvider
- Increments download count atomically
- Logs DownloadTracking record (IP + timestamp)
- Updates EgressBytes

### 3. Download Info API (for the page)

Create `src/TeleCloud.Server/Features/Downloads/Info/DownloadInfoEndpoint.cs`:
- `GET /api/downloads/{ShareCode}/info` — returns file metadata without streaming
- Response: `{ fileName, fileSize, expiresAt, downloadCount, maxDownloads }`
- 404 if not found, 410 if expired

### 4. Stream Response Pattern

Use FastEndpoints `SendStreamAsync()` with `FileStreamResult`-like approach. Since chunks are stored via IStorageProvider, stream them sequentially:
```csharp
await SendAsync(stream, contentType: delivery.ContentType, fileName: delivery.FileName, ct);
```
Or use raw response streaming via `HttpContext.Response.Body`.

---

## Dev Notes

### No Auth Required

Both endpoints under `/api/downloads/*` must NOT require authentication. Use `AllowAnonymous()` in the endpoint configuration.

### Download Tracking

On successful download:
```csharp
delivery.DownloadCount++;
delivery.EgressBytes += chunk.Size;
db.DownloadTrackings.Add(new DownloadTracking
{
    Id = Guid.CreateVersion7(),
    DeliveryId = delivery.Id,
    IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "",
    DownloadedAt = DateTime.UtcNow
});
```

### Existing to Preserve

- `Features/Files/Download/` — legacy Telegram download. DO NOT MODIFY.
- `Features/TempShares/Download/` — legacy temp share download. DO NOT MODIFY.
- All files go under `Features/Downloads/`.

### Error Handling

- 404 → Delivery not found by ShareCode
- 410 → Expired or download cap reached
- Use `NotFoundError` for 404s, custom `LightResults.Error` for others

### Testing

- Test streaming download with known chunks
- Test SHA-256 mismatch detection (corrupt a chunk in storage)
- Test download cap enforcement
- Test expired delivery rejection
- Test concurrent download counting
