---
title: 'Orphan R2 object sweep (GH #107)'
type: 'feature'
created: '2026-08-14'
status: 'done'
review_loop_iteration: 0
baseline_commit: '18ee1ff'
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `DeliveryExpiryJob` deletes R2 objects for expired deliveries and `ZombiePartSweeper` aborts multipart uploads that still have an `UploadSession` row — but nothing reclaims R2 data with **no matching DB row**: uploads that died mid-PUT, sessions hard-deleted, manual DB fixes. Storage bleeds money silently (R2 bills per GB-month).

**Approach:** A daily `OrphanSweepJob` lists R2 objects and in-flight multipart uploads under the `uploads/` prefix, cross-checks `Delivery.StoragePath` and `UploadSession.StoragePath`/`R2UploadId`, and reclaims what nothing references. Dry-run by default: it only reports until explicitly enabled via config.

## Boundaries & Constraints

**Always:**
- Sweep only keys under the `uploads/` prefix (`R2ObjectKey` layout) — never anything else in the bucket (protects #90 backups).
- Delete an object only when it is older than `OrphanObjectMinAgeDays` (default 7) AND its key matches no `Delivery.StoragePath` and no `UploadSession.StoragePath`.
- Abort a multipart upload only when initiated more than `OrphanMultipartMinAgeHours` (default 24) ago AND its UploadId matches no `UploadSession.R2UploadId`.
- Dry-run is the default (`OrphanSweepDelete = false`): log the full would-delete report, delete nothing.
- Log every deleted/aborted key and a per-cycle count (audit trail; feeds #89 alerting later).
- Snapshot DB paths AFTER listing R2, so an upload committed mid-sweep is never a false positive.

**Ask First:** Enabling delete mode in any deployed environment (flipping `OrphanSweepDelete` to true in compose/env is a human decision, not part of this change).

**Never:** No new external dependencies; no audit-log DB table (#61 owns that); no sweeping of local-disk storage (job registers only when R2 is configured); no changes to `DeliveryExpiryJob`/`ZombiePartSweeper` behavior.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Orphan object | R2 key >7d old, no Delivery/UploadSession row | Dry-run: reported. Delete mode: deleted + logged | Per-key failure logged, sweep continues |
| Linked object | R2 key >7d old, matches a `Delivery.StoragePath` | Untouched, not reported | N/A |
| Fresh orphan | R2 key 1d old, no DB row | Untouched (age guard) | N/A |
| Orphan multipart | Upload initiated >24h ago, no `UploadSession.R2UploadId` | Aborted + logged (both modes — abort is safe: session-less uploads can never complete) | Per-upload failure logged, sweep continues |
| Tracked multipart | Upload initiated >24h ago, `UploadSession` row exists | Untouched (ZombiePartSweeper owns it) | N/A |
| R2 list fails | ListObjects throws | Cycle aborts, error logged, next cycle retries | Catch-all per cycle (existing job pattern) |

</frozen-after-approval>

## Code Map

- `src/QuickShare/Infrastructure/Jobs/OrphanSweepJob.cs` -- NEW: the job (BackgroundService, follows `ZombiePartSweeper` pattern incl. `internal` sweep method for tests)
- `src/QuickShare/Infrastructure/Storage/IStorageProvider.cs` -- add `ListObjectsAsync(prefix, olderThan)` + `ListMultipartUploadsAsync(prefix, olderThan)`
- `src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs` -- implement both (paginated `ListObjectsV2` / `ListMultipartUploads`)
- `src/QuickShare/Infrastructure/Storage/LocalDiskStorageProvider.cs` -- both throw `NotSupportedException` (same as `DeleteObjectsAsync`)
- `src/QuickShare/Infrastructure/QuickShareOptions.cs` -- add `OrphanSweepDelete=false`, `OrphanObjectMinAgeDays=7`, `OrphanMultipartMinAgeHours=24`, `OrphanSweepIntervalHours=24`
- `src/QuickShare/Program.cs` -- register `OrphanSweepJob` only when R2 is configured (`hasR2` block)
- `src/QuickShare/Infrastructure/Constants/` -- reuse; no new constants file expected beyond option defaults
- `tests/QuickShare.Server.Tests/E2E/` -- new `OrphanSweepJobTests.cs` with fake `IStorageProvider` (see how existing tests fake storage)

## Tasks & Acceptance

**Execution:**
- [x] `IStorageProvider.cs` -- add the two list methods (+ small result record for multipart: `R2MultipartInfo(string UploadId, string ObjectKey, DateTime Initiated)`) -- sweep needs enumeration the interface lacks
- [x] `R2StorageProvider.cs` -- implement with pagination (`ContinuationToken` / `KeyMarker`), filter by `LastModified`/`Initiated` server-side after fetch -- R2 lists max 1000/page
- [x] `LocalDiskStorageProvider.cs` -- `NotSupportedException` stubs -- job never runs without R2
- [x] `QuickShareOptions.cs` -- four new options with safe defaults -- guardrails are config, not code edits
- [x] `OrphanSweepJob.cs` -- daily loop; list R2 first, then load DB path/uploadId sets, diff, act per mode; structured logs per item + summary count -- core feature
- [x] `Program.cs` -- `AddHostedService<OrphanSweepJob>()` inside the `hasR2` branch -- LocalDisk would throw
- [x] `OrphanSweepJobTests.cs` -- E2E: fake storage returns orphan+linked+fresh objects and tracked+untracked multiparts; assert dry-run deletes nothing, delete mode deletes exactly the orphans -- covers the I/O matrix

**Acceptance Criteria:**
- Given a deployed app with R2, when the job runs in default config, then it logs a would-delete report and deletes nothing.
- Given `OrphanSweepDelete=true`, when the job runs, then only unreferenced objects older than 7d are deleted and each deletion is logged with its key.
- Given an object referenced by an active delivery, when the sweep runs in any mode, then the object is never deleted or reported (zero false positives).
- Given local-disk storage (no R2), when the app starts, then the job is not registered and nothing throws.

## Design Notes

Order matters for the false-positive guarantee: list R2 **first**, then read DB reference sets. Any upload committed between the two steps exists in the DB snapshot, so it is excluded; an upload started after the R2 listing simply isn't in the candidate list. Age guards (7d/24h) additionally dwarf any in-flight window.

## Verification

**Commands:**
- `dotnet build QuickShare.slnx` -- expected: 0 errors, 0 warnings
- `dotnet test tests/QuickShare.Server.Tests --filter FullyQualifiedName~OrphanSweep` -- expected: all pass
- `dotnet format QuickShare.slnx --verify-no-changes` -- expected: no diff

## Suggested Review Order

**Sweep algorithm & safety guarantees**

- Entry point: list-R2-first ordering, reference-set diff, dry-run gate — the whole design in one method
  [`OrphanSweepJob.cs:43`](../../src/QuickShare/Infrastructure/Jobs/OrphanSweepJob.cs#L43)

- Circuit breaker: candidate spike downgrades the cycle to dry-run instead of mass-deleting
  [`OrphanSweepJob.cs:76`](../../src/QuickShare/Infrastructure/Jobs/OrphanSweepJob.cs#L76)

- Exact-key delete — review flagged prefix-delete as a data-loss vector; this is the fix
  [`OrphanSweepJob.cs:100`](../../src/QuickShare/Infrastructure/Jobs/OrphanSweepJob.cs#L100)

- 15-min initial delay so daily redeploys still sweep (review finding: delay-first loop never ran)
  [`OrphanSweepJob.cs:19`](../../src/QuickShare/Infrastructure/Jobs/OrphanSweepJob.cs#L19)

**Storage provider surface**

- New exact-key delete — the only deletion API the sweep is allowed to use
  [`R2StorageProvider.cs:251`](../../src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs#L251)

- UTC-kind normalization: wrong DateTime.Kind would silently shrink the age safety margin
  [`R2StorageProvider.cs:267`](../../src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs#L267)

- Paginated object listing with null-collection guard
  [`R2StorageProvider.cs:272`](../../src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs#L272)

- Multipart listing: client-side prefix re-filter (distrust R2 compat) + truncation-marker loop guard
  [`R2StorageProvider.cs:307`](../../src/QuickShare/Infrastructure/Storage/R2StorageProvider.cs#L307)

- Interface contract for the three new members
  [`IStorageProvider.cs:50`](../../src/QuickShare/Infrastructure/Storage/IStorageProvider.cs#L50)

**Wiring & config**

- Job registered only when R2 is configured — local disk would throw
  [`Program.cs:184`](../../src/QuickShare/Program.cs#L184)

- All guardrails as options; `OrphanSweepDelete` defaults false
  [`QuickShareOptions.cs:12`](../../src/QuickShare/Infrastructure/QuickShareOptions.cs#L12)

**Tests**

- Delete mode touches exactly the orphan, never linked/session-referenced keys
  [`OrphanSweepJobTests.cs:93`](../../tests/QuickShare.Server.Tests/E2E/OrphanSweepJobTests.cs#L93)

- Prefix confinement + 7d/24h age cutoffs asserted against the recording fake
  [`OrphanSweepJobTests.cs:107`](../../tests/QuickShare.Server.Tests/E2E/OrphanSweepJobTests.cs#L107)
