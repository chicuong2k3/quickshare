---
id: SPEC-r2-storage
companions:
  - upload-flow.md
  - download-flow.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# Cloudflare R2 Blob Storage Integration

## Why

**A pain to solve.** The TeleCloud server currently proxies every file byte through local disk — both during upload (chunked via server) and download (streamed via server). This consumes server bandwidth linearly with file size. The operator wants to self-host the application on a home server with limited upstream bandwidth, where a single 10GB transfer would saturate the connection for minutes. Cloudflare R2 eliminates server egress cost ($0/TB) and bandwidth consumption by having clients interact directly with R2 via presigned URLs. The server becomes a lightweight control plane handling only metadata and signed URLs.

## Capabilities

- **CAP-1 — Presigned upload**
  - **intent:** Client can upload file chunks directly to R2 via presigned PUT URLs without the server proxying bytes.
  - **success:** A 1GB file uploaded via the app completes without the server receiving more than 10KB of transfer (headers + metadata).

- **CAP-2 — Presigned download**
  - **intent:** Download requests receive a redirect to a signed R2 URL, so the file is served directly from R2's edge network.
  - **success:** Download response is a 302 redirect to `r2.dev` (or custom domain) with a 60-second signed URL. File bytes never pass through the server.

- **CAP-3 — S3 multipart for large files**
  - **intent:** Files larger than 10MB use S3 multipart upload with presigned URLs per part, uploaded in parallel by the client.
  - **success:** A 5GB file upload completes with 200+ parts, each part PUT directly to R2, verified via `CompleteMultipartUpload`.

- **CAP-4 — Storage backend switchable via config**
  - **intent:** Operator can toggle between R2 and local disk by changing a config value, with zero code changes.
  - **success:** Setting `Storage:Provider: R2` uses R2 for all new uploads/downloads; setting `Storage:Provider: Local` uses existing local-disk path. Existing Delivery records served from whichever backend they were stored on.

- **CAP-5 — Server verifies before committing**
  - **intent:** Server validates that all parts exist on R2 (via `HeadObject` or `ListParts`) before creating the Delivery record.
  - **success:** If a client claims completion but R2 confirms missing parts, the commit endpoint returns 400 and does not create a Delivery.

- **CAP-6 — Backward-compatible download routing**
  - **intent:** Existing deliveries stored on local disk remain downloadable without migration.
  - **success:** A Delivery created before the R2 migration still serves its file from local disk. A Delivery created after migration serves from R2. The download endpoint routes based on `StoragePath` column (null = local, non-null = R2).

## Constraints

- Server must never proxy file bytes — only metadata, signed URLs, and control signals.
- Must use existing `IStorageProvider` interface — `R2StorageProvider` implements the same contract as `LocalDiskStorageProvider`.
- R2 is S3-compatible via `AWSSDK.S3` — no custom API client needed.
- Presigned URLs must have short TTL (60s for download, 15 min per part for upload) to prevent misuse.

## Non-goals

- P2P transfer path is unchanged — remains direct peer-to-peer, no R2 involvement.
- Existing Delivery records on local disk are not migrated — backward compat is handled at read time.
- No CDN layer on top of R2 — R2's global edge network is sufficient.
- No client-side encryption changes — existing PIN-based encryption (AD-5) is independent of storage backend.

## Success signal

A new upload completes end-to-end: client receives presigned URLs, PUTs chunks directly to R2 without server byte proxy, commits successfully, and the resulting share link serves a download that redirects to R2 with zero server egress. A pre-migration Delivery continues to download from local disk without any operator intervention.

## Assumptions

- AWSSDK.S3 works with R2's S3-compatible API without custom HTTP handling. Verified by community documentation.
- R2's free egress tier ($0/TB) remains stable for the foreseeable future.

## Open Questions

<!-- None. All resolved. -->
