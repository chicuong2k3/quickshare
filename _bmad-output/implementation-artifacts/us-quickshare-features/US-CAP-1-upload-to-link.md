# US-CAP-1 — Upload-to-link (core)

Source: `SPEC-quickshare-features` → CAP-1

**Story**
As a sender, I want to upload a file by dragging it onto the page or pasting an image, and get a share link back immediately, so that I can share the file without signing up or filling forms.

**Acceptance criteria**
- [ ] Upload starts immediately on drop/paste — no form fields required before upload begins.
- [ ] File lands on R2 storage via the chunked/multipart path; success returns a share link with an unguessable (opaque token) URL.
- [ ] Anonymous senders are capped at 100 MB (`PerkOwnerLimits`); signed-in senders capped by plan (`PlanTierLimits`).
- [ ] Works from mobile and desktop browsers.
- [ ] Over-cap upload shows a clear error + upgrade path (no silent failure).

**Constraints in force**
- HTMX lazy-load default; `hx-get` + `hx-select` on every partial request.
- E2E encryption by default (CAP-2) — upload path encrypts client-side; no plaintext toggle.
- FusionCache TTL + invalidation after `SaveChangesAsync`.
- No magic strings — route paths / protocol identifiers via `RoutePaths` / constants.
- Strings via `SharedLoc.{en,vi}.resx`.

**Test anchors**
- Fresh browser (clean session): drag → link in clipboard (BrowserAppFixture + Playwright).
- Unit: chunk upload handler quota enforcement for anonymous vs signed-in.

---

## Implementation Flow

1. **Landing** — `GET /send` serves `Upload.razor` (MainLayout) as a static JS-driven shell: drop zone, paste hint, OTP (6 ô, optional via `otp_name`/`otp_num`), success share-box (`#qrcode`, copy, encrypt badge), cancel/resume. Scripts: `paste-anywhere.js` + `upload.js`; `window.ZAPDROP_CONFIG = { apiBaseUrl: "" }`; `<AdComponent />` renders for paid/consent states.
2. **Client capture** — file input / drag-drop / `qsPaste.fileFromClipboard` (image/* → `pasted-{stamp}.{ext}`, text/plain → `.txt`; `/send#paste` handoff) → `startUpload(source: 'web'|'paste')`; client guard `MAX_FILE_BYTES 5GB`.
3. **Client-side encryption (hand-off to CAP-2)** — `upload.js` generates an AES-256-GCM key, encrypts the whole file in memory → `[nonce 12B][ciphertext]` blob; raw key exported → base64url → appended to link as `#key=` fragment (never sent to server).
4. **Initiate multipart** — POST `/api/uploads/start-r2` (ended public: `AllowAnonymous()`); validator: FileName/ContentType/PartNumbers non-empty + FileSize>0. Handler: `UploadConstants.DefaultChunkSize` 25MB; `GetMaxFileSize(plan)` gate; `MaxPartsFor = min(10_000, ceil(FileSize/MinPartBytes)+1)` with `MinPartBytes` 5MB; `deliveryId = Guid.CreateVersion7()`; `R2StorageProvider.InitR2MultipartUploadAsync` → presigned PUT URLs per part; R2 failure → 502, other validation → 400.
5. **Parallel upload** — `upload.js` uploads 4 concurrent presigned PUTs (`CONCURRENT_PARTS`), `PART_RETRY_ATTEMPTS 3` (backoff + refresh part URLs via `/api/uploads/part-urls` before retry), strips ETag quotes.
6. **Commit** — POST `/api/uploads/commit-r2` → `CommitR2Handler`:
   - Guard: session null / Status ≠ InProgress / R2UploadId mismatch / StoragePath mismatch / SenderId mismatch → "No matching in-progress upload session".
   - `CompleteR2MultipartUploadAsync` (ListParts pre-check → "No parts found on R2" → best-effort delete object + fail).
   - Plan limit re-enforced on **actual** size via `GetObjectSizeAsync` — not declared part sizes; over → `DeleteObjectsAsync` and fail.
   - Build `Delivery` from **session** metadata (not request): `ChunkCount = Parts.Count`, `MaxDownloads = GetMaxDownloads(plan)`, `StoragePath = ObjectKey`, `ExpiresAt = UtcNow + GetMaxExpiry(plan)`, optional `OtpHash = BCrypt.HashString`, `Source = DeliverySources.Normalize(cmd.Source)`; session → Committed, R2UploadId cleared.
   - `shareCode` = 8 random bytes Base64Url (11 chars); `ShareLink = {scheme}://{host}/d/{ShareCode}`; **201 + Location `/api/downloads/{ShareCode}`**.
7. **Result UX** — share-box + QR (inline SVG) + auto copy link; on invalid session/cancel → cancel button issues `/api/uploads/abort-r2` (mismatch = silent no-op, session not deleted).
8. **Quota/caps (hand-off to CAP-6)** — `plan = user?.Plan ?? PlanTierLimits.Anonymous`; anonymous ceiling `PerkOwnerLimits` 100MB enforced server-side in start/commit handlers — client trust never assumed.

## Code Structure

- `Features/Uploads/StartR2/StartR2Endpoint.cs`, `StartR2Handler.cs`, `StartR2Validator.cs` — initiate multipart; quota/part-count gates.
- `Features/Uploads/PartUrls/{PartUrlsEndpoint,PartUrlsHandler,PartUrlsValidator}.cs` — refresh presigned part URLs (distinct endpoint per handler).
- `Features/Uploads/AbortR2/{AbortR2Endpoint,AbortR2Handler,AbortR2Validator}.cs` — abort multipart + delete session row.
- `Features/Uploads/CommitR2/{CommitR2Endpoint,CommitR2Handler,CommitR2Validator}.cs` — guard, complete R2, create `Delivery`, 201 + Location.
- `Features/Uploads/UploadConstants.cs` — 25MB chunk, 5MB min part, part-count ceiling.
- `Components/Pages/Upload.razor` + `wwwroot/js/upload.js` + `wwwroot/js/paste-anywhere.js` — client flow.
- `Infrastructure/Storage/{IStorageProvider,R2StorageProvider,LocalDiskStorageProvider,R2Options,R2ObjectKey,R2StorageException}.cs` — multipart contract.
- `Infrastructure/Constants/{PlanTierLimits,RoutePaths,DeliverySources,MediaTypes}.cs` — limits, routes, protocol strings.
