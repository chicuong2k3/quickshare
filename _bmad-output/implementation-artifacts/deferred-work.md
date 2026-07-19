# Deferred Work

Tracked items that were identified during reviews and deferred for later implementation.

---

## Deferred from: code review of 1-1-storage-backend-foundation (2026-07-19)

- `LocalDiskStorageProvider` uses `File.ReadAllBytesAsync` loading entire chunk into memory — acceptable for initial impl, revisit if chunk sizes exceed 50MB
- No index on `Delivery.CreatedAt` — add when query patterns are known (no queries filter by CreatedAt yet)
- `UploadSession.Status` is a free-text string — convert to enum in a later story when status logic is implemented
