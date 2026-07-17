---
title: 'QuickShareCloud — anonymous temp file sharing with auto-expiry'
type: 'feature'
created: '2026-07-18'
status: 'in-review'
baseline_revision: '924f56f02feadf520544e18d32676eab5822e8ca'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-quicksharecloud/SPEC.md'
  - '{project-root}/_bmad-output/specs/spec-quicksharecloud/ux-flow.md'
  - '{project-root}/_bmad-output/specs/spec-quicksharecloud/server-changes.md'
warnings:
  - oversized
---

<intent-contract>

## Intent

**Problem:** TeleCloud has no way to share a file anonymously with auto-expiry — every share today assumes an authenticated folder owner. QuickShareCloud adds a drag-drop/paste → link-in-clipboard → QR flow where files live in Telegram for 24h or 7d and vanish after, plus a one-line `curl` API for developers.

**Approach:** Add a `TempShare` join table linking an existing `files` row to an expiry timestamp; expose three anonymous FastEndpoints (`POST /api/temp`, `GET /api/temp/{id}`, `GET /api/temp/{id}/download`) mirroring the current upload/download handlers; add a `BackgroundService` that deletes expired Telegram messages + DB rows every 15 min; add a separate Blazor .NET 10 Static SSR project (`TeleCloud.Web`, RizzyUI + ArcticTheme + Alpine.js + Tailwind 4) that renders the upload page `/` and the download page `/t/{id}` and calls the API over HTTP.

## Boundaries & Constraints

**Always:**
- Server-enforced expiry: the download endpoint (`GET /api/temp/{id}/download`) is the hard gate — `ExpiresAt <= DateTime.UtcNow` ⇒ `410 Gone`. Never trust the client for cutoff.
- Share ids are full `Guid.CreateVersion7()` (opaque, unguessable). The `a1b2c3d4` token in `server-changes.md` examples is illustrative, not a literal 8-char id — see Design Notes.
- Mirror existing FastEndpoints/Wolverine/LightResults patterns exactly: thin `Endpoint<TReq,TResp>` dispatches `IMessageBus`; handler is a plain class taking `TeleCloudDbContext` + `TelegramService`; errors are `LightResults` errors translated via `ThrowError`/status codes.
- Telegram delete uses `telegram.Client.Channels_DeleteMessages(channel.ToInputChannel(), [messageIds])` directly (no `TelegramService.DeleteAsync` exists).
- Backend builds clean (`dotnet build`) and the cleanup/expiry logic is unit-tested.
- Frontend is Blazor Static SSR (no `InteractiveServer`, no WebAssembly); interactivity via Alpine.js; QR generated client-side; clipboard via `navigator.clipboard`; upload progress via `XMLHttpRequest`.
- Add an EF Core migration (`AddTempShares`); do not hand-write raw SQL — `Program.cs` applies pending migrations at startup.

**Block If:**
- The RizzyUI NuGet package (`RizzyUI`) or NPM package (`@jalexsocial/rizzyui`) cannot be restored, or the Tailwind 4 CSS build pipeline cannot produce output. (Frontend cannot be verified without it.)
- A live Telegram account + PostgreSQL are required to *run* anything — they are NOT required to build/test; if a build-time dependency is unsatisfiable, HALT.

**Never:**
- No user accounts, login, or registration (non-goal).
- No permanent storage — every temp share expires and is purged.
- No file preview/thumbnail/transcoding, no batch upload, no email notification, no DMCA system (non-goals).
- No Blazor WebAssembly or InteractiveServer rendering for this feature (constraint).
- No external QR/short-link service (constraint — QR is client-side).
- Do not add delete to `TelegramService` as a new abstraction just for this; call the client directly to match `DeleteFileHandler`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Upload happy path (24h) | `POST /api/temp` multipart `file` (5 MB pdf), no expiry | `201` `{ id, link, filename, size, expiresAt=now+24h, downloadUrl }` | None |
| Upload 7-day expiry | same, `expiry=7d` | `201`, `expiresAt = now + 7d` | None |
| Upload default expiry | no `expiry` field | treated as `24h` | None |
| Upload too large | file > 2 GB | — | `413` (size checked before Telegram call) |
| Upload missing file | no `file` part | — | `400` |
| Upload invalid expiry | `expiry=3h` | — | `400` (only `24h`/`7d` accepted) |
| Media rejected | image/video Telegram media | — | `400` (mirror existing upload: only documents) |
| Get metadata live | `GET /api/temp/{id}` before expiry | `200` `{ id, filename, size, contentType, expiresAt, expired:false }` | None |
| Get metadata expired | row exists, `ExpiresAt` passed | `200` with `expired:true` (filename still shown for UX expired state) | None |
| Get metadata missing | no such id | — | `404` |
| Download live | `GET /api/temp/{id}/download` before expiry | `200` streamed, `Content-Type`, `Content-Disposition: attachment` | None |
| Download with Range | `Range: bytes=0-1023` | `206`, `Content-Range`, partial stream | None |
| Download expired | past expiry | — | `410 Gone` |
| Download missing | no such id | — | `404` |
| Cleanup run | `TempShareCleanupService` tick, expired rows exist | Telegram messages deleted, `files` rows deleted, `TempShares` cascade-deleted, stats logged | Per-row failure logged, batch continues |
| Upload page paste | browser Ctrl+V an image | image uploaded as a file, success state | Non-image clipboard → tooltip "No image in clipboard" |

</intent-contract>

## Code Map

Backend (existing — mirror, do not break):
- `src/TeleCloud.Server/Features/Files/Upload/UploadEndpoint.cs` + `UploadFileHandler.cs` — pattern to copy for `POST /api/temp` (multipart, `IMessageBus`, Telegram upload via `telegram.Client.UploadFileAsync`+`SendMediaAsync`, media-reject, create `FileMetadata`).
- `src/TeleCloud.Server/Features/Files/Download/DownloadEndpoint.cs` + `DownloadFileHandler.cs` — pattern to copy for streaming + range (`Upload_GetFile` loop writing to `HttpContext.Response.Body`).
- `src/TeleCloud.Server/Features/Files/Delete/` (`DeleteFileHandler`) — shows `telegram.Client.Channels_DeleteMessages` usage for cleanup.
- `src/TeleCloud.Server/Services/TelegramService.cs` — exposes `Client` + `GetChannelAsync()`; no delete method.
- `src/TeleCloud.Server/Data/TeleCloudDbContext.cs` + `FileMetadata.cs` + `Folder.cs` — add `TempShare` entity + DbSet here.
- `src/TeleCloud.Server/Migrations/` + `TeleCloudDbContextModelSnapshot.cs` — add migration `AddTempShares`.
- `src/TeleCloud.Server/Program.cs` — register `TempShareCleanupService` (`AddHostedService`) + CORS for the web origin + `QuickShare` options.
- `src/TeleCloud.Server/GlobalUsings.cs` — add `TeleCloud.Server.Features.TempShares.Shared`.

Backend (new):
- `src/TeleCloud.Server/Data/TempShare.cs` — entity.
- `src/TeleCloud.Server/Features/TempShares/Create/` — endpoint + handler + `CreateTempShareCommand` + `TempShareResponse`.
- `src/TeleCloud.Server/Features/TempShares/Get/` — endpoint + handler + `GetTempShareQuery`.
- `src/TeleCloud.Server/Features/TempShares/Download/` — endpoint + handler + `DownloadTempShareQuery`.
- `src/TeleCloud.Server/Features/TempShares/Shared/` — `TempShareResponse`, `TempShareInfoResponse`, mapper, errors.
- `src/TeleCloud.Server/Infrastructure/TempShareCleanupService.cs` — `BackgroundService`.
- `src/TeleCloud.Server/Migrations/<ts>_AddTempShares.cs` (+ `.Designer.cs`, snapshot regen).
- `src/TeleCloud.Server/appsettings.Development.json` — `QuickShare:PublicBaseUrl`, CORS origins.

Frontend (new project):
- `src/TeleCloud.Web/TeleCloud.Web.csproj` — Blazor Static SSR + RizzyUI + `HttpClient`; added to `TeleCloud.slnx`.
- `src/TeleCloud.Web/Program.cs` — `AddRazorComponents().AddStaticServerRendering()`, RizzyUI services, `RzHeadOutlet`, antiforgery, `HttpClient` w/ `ApiBaseUrl`.
- `src/TeleCloud.Web/App.razor`, `Routes.razor`, `_Imports.razor`, `Components/`, `Pages/Upload.razor`, `Pages/Download.razor`.
- `src/TeleCloud.Web/wwwroot/js/upload.js` — drag-drop, paste, XHR progress, clipboard, QR.
- `src/TeleCloud.Web/wwwroot/lib/qrcode/` — vendored client-side QR lib.
- `src/TeleCloud.Web/package.json` + `tailwind` CSS entry + build script; `wwwroot/css/app.css` (generated).

Tests (new — establish pattern in the empty project):
- `tests/TeleCloud.Server.Tests/TempShares/TempShareExpiryTests.cs` — unit tests for expiry math + 410/404 gating logic.
- `tests/TeleCloud.Server.Tests/TempShares/TempShareCleanupServiceTests.cs` — fake db/telegram, assert expired rows + messages are deleted.

## Tasks & Acceptance

**Execution:**
- [x] `src/TeleCloud.Server/Data/TempShare.cs` -- create `TempShare` record entity (`Id`, `FileId`, `ExpiresAt`, `CreatedAt`) -- stores share metadata.
- [x] `src/TeleCloud.Server/Data/TeleCloudDbContext.cs` -- add `DbSet<TempShare> TempShares` + `OnModelCreating` config (PK `Id`, FK `FileId→files.Id OnDelete(Cascade)`, index on `ExpiresAt`) -- data model per server-changes.md.
- [x] `src/TeleCloud.Server/Features/TempShares/Shared/*` -- add `TempShareResponse`, `TempShareInfoResponse`, mapper, reuse/extend `Errors` -- shared DTOs.
- [x] `src/TeleCloud.Server/Features/TempShares/Create/CreateTempShareEndpoint.cs` + `CreateTempShareHandler.cs` -- `POST /api/temp` multipart (`file`+`expiry`), upload to Telegram, create `FileMetadata` (root folder) + `TempShare`, return `201` with link/downloadUrl/expiresAt -- CAP-1/4/5/7.
- [x] `src/TeleCloud.Server/Features/TempShares/Get/GetTempShareEndpoint.cs` + handler -- `GET /api/temp/{id}` returns info, `expired` flag, `404` if absent -- CAP-6 download page data.
- [x] `src/TeleCloud.Server/Features/TempShares/Download/DownloadTempShareEndpoint.cs` + handler -- `GET /api/temp/{id}/download`, `410` if expired, `404` if absent, stream + range (mirror `DownloadFileHandler`) -- CAP-4/6 hard expiry gate.
- [x] `src/TeleCloud.Server/Infrastructure/TempShareCleanupService.cs` -- `BackgroundService`, 15-min timer, delete expired Telegram messages + `files` rows, log stats -- CAP-4 cleanup.
- [x] `src/TeleCloud.Server/Program.cs` -- `AddHostedService<TempShareCleanupService>()`, `AddCors` (allow configured web origins), bind `QuickShare` options -- wire service + cross-origin web client.
- [x] `src/TeleCloud.Server/GlobalUsings.cs` -- add `TeleCloud.Server.Features.TempShares.Shared` -- namespace convenience.
- [x] `src/TeleCloud.Server/appsettings.Development.json` -- add `QuickShare:PublicBaseUrl` + `QuickShare:AllowedCorsOrigins` -- dev config for link generation + CORS.
- [x] generate migration -- `dotnet ef migrations add AddTempShares -p src/TeleCloud.Server` -- persist schema.
- [x] `tests/TeleCloud.Server.Tests/TempShares/TempShareExpiryTests.cs` -- unit-test expiry calc + gate logic (410/404/200-expired) -- I/O matrix edge cases.
- [x] `tests/TeleCloud.Server.Tests/TempShares/TempShareCleanupServiceTests.cs` -- fake db + fake telegram, assert expired rows deleted, unexpired kept -- CAP-4 cleanup correctness.
- [x] `src/TeleCloud.Web/` (new project) -- Blazor Static SSR host with RizzyUI + ArcticTheme, `Program.cs` wiring, `App.razor`/`Routes`/`_Imports`, added to `.slnx` -- CAP-1/2/3/5/6 frontend.
- [x] `src/TeleCloud.Web/Pages/Upload.razor` + `wwwroot/js/upload.js` -- drop zone, expiry toggle, drag-drop/paste/XHR-progress, success state with link + QR (client-side) + auto-copy -- CAP-1/2/3/5.
- [x] `src/TeleCloud.Web/Pages/Download.razor` -- SSR metadata fetch → Ready/Expired/NotFound states, download links to API `GET /api/temp/{id}/download` -- CAP-6.
- [x] `src/TeleCloud.Web/package.json` + Tailwind 4 build script + `wwwroot/css/app.css` -- RizzyUI Tailwind pipeline (ArcticTheme) -- constraint: RizzyUI SSR + Tailwind 4.

**Acceptance Criteria:**
- Given a clean build, when `dotnet build TeleCloud.slnx` runs, then all projects compile with zero errors.
- Given the test project, when `dotnet test` runs, then expiry-gate and cleanup tests pass.
- Given an expired share id, when `GET /api/temp/{id}/download` is called, then the response is `410 Gone` (server enforces expiry, not the client).
- Given a valid share id, when `GET /api/temp/{id}` is called, then the response is `200` with `expired:false` and the filename/size/contentType/expiresAt.
- Given expired rows in the DB, when `TempShareCleanupService.ExecuteAsync` runs, then those Telegram messages are deleted and the `files`/`TempShares` rows are removed; unexpired rows are untouched.
- Given a file dropped on the upload page, when the XHR upload completes, then the share link is auto-copied to the clipboard and a client-rendered QR code appears beside it.
- Given a share link `/t/{id}`, when opened, then the download page renders filename, human-readable size, expiry countdown, and a one-click download button (or the Expired/NotFound state).

## Design Notes

**Coherence resolutions (companion vs companion):**
1. *Expired metadata GET.* `server-changes.md` says `GET /api/temp/{id}` returns `404 — share not found or expired`, yet its own 200 body includes an `expired` boolean, and `ux-flow.md` requires the Expired state to show the filename + expiry time. These conflict. Resolution: the metadata endpoint returns `200` with `expired:true` when the row exists but is past expiry (so the page can render the Expired state with filename), and `404` only when the row truly does not exist. The **download** endpoint remains the hard gate and returns `410 Gone` on expiry. This honors both companions without losing data the UX needs.
2. *Share id entropy.* `server-changes.md` examples show `"id":"uuid-v7"` (POST) and `"id":"a1b2c3d4"` (GET). The SPEC constraint demands unguessable opaque ids. An 8-hex-char id is only 32 bits — not unguessable. Resolution: use the full `Guid.CreateVersion7()` as the route id and in responses; the `a1b2c3d4` token is treated as an illustrative truncation, not a spec for id length.

**Temp files live in the root folder `/`.** The `files.folder_id` FK is `NOT NULL` and `OnDelete(Restrict)`. Temp uploads reuse the always-seeded root folder (`/`) rather than creating a per-share folder, minimizing writes. `TempShare.FileId → files.Id` is `OnDelete(Cascade)`, so deleting the `files` row removes the `TempShare` automatically — cleanup deletes the file row, not the share row.

**`link` vs `downloadUrl`.** `downloadUrl` is the API's own download endpoint, built from the request scheme/host. `link` is the human-facing web URL `{QuickShare:PublicBaseUrl}/t/{id}`; `PublicBaseUrl` defaults to the web app origin and is configurable (the API cannot otherwise know the web origin). In dev, set it in `appsettings.Development.json`.

**Architecture note (deviation from a SPEC assumption).** The SPEC *assumes* the Blazor SSR app is a separate project deployed standalone calling the API. This spec keeps the **separate project** (`TeleCloud.Web`) to honor that intent and to leave the verified `TeleCloud.Server` untouched/low-risk, but co-locates both in the existing solution and enables permissive CORS in dev so the browser can upload directly to the API for real progress events. This loses no capability and improves verifiability.

**QR + clipboard are client-side only** (`ux-flow.md`): vendor a small QR library under `wwwroot/lib/qrcode/` rendering inline SVG; clipboard via `navigator.clipboard.writeText`; upload progress via `XMLHttpRequest.upload.onprogress`. No server round-trip for these.

**Verification ceiling (honest).** A background run can `dotnet build` + `dotnet test` + run the Tailwind CSS build, but cannot exercise a live Telegram account or PostgreSQL or a browser. E2E upload/download is therefore not runtime-verified here — only compile, unit-test, and asset-build success.

## Verification

**Commands:**
- `dotnet build TeleCloud.slnx` -- expected: all projects build, zero errors.
- `dotnet test tests/TeleCloud.Server.Tests/TeleCloud.Server.Tests.csproj` -- expected: expiry-gate + cleanup tests pass.
- `dotnet ef migrations add AddTempShares -p src/TeleCloud.Server -s src/TeleCloud.Server` -- expected: migration + designer + updated snapshot generated (run during impl, not as a gate).
- `cd src/TeleCloud.Web && npm install && npm run build:css` -- expected: `wwwroot/css/app.css` produced (Tailwind 4 + RizzyUI ArcticTheme).

**Manual checks (if no CLI):**
- Confirm `TempShareCleanupService` is registered and `POST/GET /api/temp*` routes appear in Swagger at `/swagger` (read code + endpoint `Configure`).
- Confirm no `InteractiveServer`/`RenderMode` references in `TeleCloud.Web` (Static SSR only).
