# Deferred Work

Tracked items that were identified during reviews and deferred for later implementation.

---

## Deferred from: code review of 1-1-storage-backend-foundation (2026-07-19)

- `LocalDiskStorageProvider` uses `File.ReadAllBytesAsync` loading entire chunk into memory — acceptable for initial impl, revisit if chunk sizes exceed 50MB
- No index on `Delivery.CreatedAt` — add when query patterns are known (no queries filter by CreatedAt yet)
- `UploadSession.Status` is a free-text string — convert to enum in a later story when status logic is implemented
- source_spec: `_bmad-output/implementation-artifacts/spec-gh-107-orphan-r2-sweep.md`
  summary: OrphanSweepJob materializes all stale keys and all DB StoragePaths in memory per cycle; switch to per-page streaming if the bucket reaches millions of objects.
  evidence: Review finding (Blind Hunter #6 / Edge Case Hunter #10) — correct at current scale, marked with a ponytail ceiling comment in OrphanSweepJob.cs.
