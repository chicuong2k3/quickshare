---
baseline_commit: a1ecb33283f306f05d7d7145e87fb54cf4800de7
---

# Story 1.2: Harden FastEndpoints Endpoints

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an **API consumer**,
I want **endpoints that create shares or download files to reject unauthenticated requests**,
So that **anonymous users cannot upload or access protected resources**.

## Acceptance Criteria

1. **AC-1 (CreateTempShare 401):** Given I send a POST to `/api/temp` without a valid auth cookie, when the request reaches the server, then I receive a 401 response.

2. **AC-2 (DownloadFile 401):** Given I send a GET to `/api/files/{id}/download` without a valid auth cookie, when the request reaches the server, then I receive a 401 response.

3. **AC-3 (GetTempShare metadata public):** Given I send a GET to `/api/temp/{id}` without a valid auth cookie, when the request reaches the server, then I receive a 200 response with share metadata.

4. **AC-4 (DownloadTempShare public):** Given I send a GET to `/api/temp/{id}/download` without a valid auth cookie, when the request reaches the server, then I receive a 200 response with the file.

5. **AC-5 (UploadFile already protected):** Given I send a POST to `/api/files` without a valid auth cookie, when the request reaches the server, then I receive a 401 response (this endpoint already lacks AllowAnonymous — verify only).

6. **AC-6 (Authenticated CreateTempShare succeeds):** Given I send a POST to `/api/temp` WITH a valid auth cookie, when the request reaches the server, then the upload proceeds normally (not blocked by auth).

7. **AC-7 (Authenticated DownloadFile succeeds):** Given I send a GET to `/api/files/{id}/download` WITH a valid auth cookie, when the request reaches the server, then the download proceeds normally.

## Tasks / Subtasks

- [x] **Task 1: Remove AllowAnonymous from CreateTempShareEndpoint** (AC: 1, 6)
  - [x] Edit `src/TeleCloud.Server/Features/TempShares/Create/CreateTempShareEndpoint.cs`
  - [x] Remove line 16: `AllowAnonymous();`
  - [x] Verify no other changes needed to the endpoint logic

- [x] **Task 2: Remove AllowAnonymous from DownloadFileEndpoint** (AC: 2, 7)
  - [x] Edit `src/TeleCloud.Server/Features/Files/Download/DownloadEndpoint.cs`
  - [x] Remove line 8: `AllowAnonymous();`
  - [x] Verify no other changes needed to the endpoint logic

- [x] **Task 3: Verify public endpoints keep AllowAnonymous** (AC: 3, 4)
  - [x] Verify `GetTempShareEndpoint.cs` retains `AllowAnonymous()` (line 8) — no change
  - [x] Verify `DownloadTempShareEndpoint.cs` retains `AllowAnonymous()` (line 8) — no change
  - [x] Verify `UploadFileEndpoint.cs` does NOT have `AllowAnonymous()` — already protected, no change

- [x] **Task 4: Verify API auth config** (AC: 1-7)
  - [x] Read `ApiCookieAuthExtensions.cs` — confirmed `OnRedirectToLogin` returns 401, `OnRedirectToAccessDenied` returns 403
  - [x] No changes needed per AD-7

- [x] **Task 5: Build verification** (AC: 1-7)
  - [x] Run `dotnet build src/TeleCloud.Server/TeleCloud.Server.csproj` — compilation succeeded (pre-existing file lock on exe copy, not caused by this change)
  - [x] Run `dotnet build` on TeleCloud.Web — succeeded, 0 warnings, 0 errors

## Dev Notes

### Current Endpoint States

| Endpoint | File | Current AllowAnonymous | Action | Route |
|----------|------|----------------------|--------|-------|
| CreateTempShare | `Features/TempShares/Create/CreateTempShareEndpoint.cs` | Yes (line 16) | **REMOVE** | POST /api/temp |
| DownloadFile | `Features/Files/Download/DownloadEndpoint.cs` | Yes (line 8) | **REMOVE** | GET /api/files/{id}/download |
| GetTempShare | `Features/TempShares/Get/GetTempShareEndpoint.cs` | Yes (line 8) | **KEEP** | GET /api/temp/{id} |
| DownloadTempShare | `Features/TempShares/Download/DownloadTempShareEndpoint.cs` | Yes (line 8) | **KEEP** | GET /api/temp/{id}/download |
| UploadFile | `Features/Files/Upload/UploadEndpoint.cs` | No | **NO CHANGE** | POST /api/files |

### API Auth Behavior (ApiCookieAuthExtensions.cs)

The existing `AddApiCookieAuth()` extension already configures cookie auth to return proper HTTP status codes for APIs:

```csharp
options.Events.OnRedirectToLogin = ctx =>
{
    ctx.Response.StatusCode = 401;  // Not a 302 redirect
    return Task.CompletedTask;
};
options.Events.OnRedirectToAccessDenied = ctx =>
{
    ctx.Response.StatusCode = 403;  // Not a 302 redirect
    return Task.CompletedTask;
};
```

This means removing `AllowAnonymous()` from an endpoint will cause FastEndpoints to return 401 for unauthenticated requests — exactly what the ACs require. **No changes to `ApiCookieAuthExtensions.cs` are needed.**

### How FastEndpoints Handles Auth

When `AllowAnonymous()` is present in `Configure()`, the endpoint bypasses `[Authorize]` middleware entirely. When removed:
1. `app.UseAuthentication()` validates the `.AspNetCore.TeleCloud` cookie
2. `app.UseAuthorization()` checks `[Authorize]` on the endpoint
3. Unauthenticated requests get 401 (from `OnRedirectToLogin` override)
4. Authenticated requests proceed normally

### What This Story Does NOT Do

- Does not add `AuthPageRedirectMiddleware` (Story 1.1 — already done)
- Does not add OwnerId to TempShare (Epic 2)
- Does not add rate limits (Epic 3)
- Does not change any Razor page content
- Does not modify cookie auth or OAuth configuration
- Does not touch `GetTempShareEndpoint` or `DownloadTempShareEndpoint` — those stay public

### Previous Story Learnings (1.1)

- Created `AuthPageRedirectMiddleware.cs` for Blazor SSR page-level redirects
- That middleware handles browser navigation; this story handles API-level auth
- Both are needed: middleware catches page requests, `[Authorize]` catches API requests
- No test projects exist in repo — verification is `dotnet build` only

### Files to Modify

| File | Change |
|------|--------|
| `src/TeleCloud.Server/Features/TempShares/Create/CreateTempShareEndpoint.cs` | Remove `AllowAnonymous()` (line 16) |
| `src/TeleCloud.Server/Features/Files/Download/DownloadEndpoint.cs` | Remove `AllowAnonymous()` (line 8) |

### Files to Verify (No Changes)

| File | Why |
|------|-----|
| `src/TeleCloud.Server/Features/TempShares/Get/GetTempShareEndpoint.cs` | Must keep `AllowAnonymous()` — public metadata |
| `src/TeleCloud.Server/Features/TempShares/Download/DownloadTempShareEndpoint.cs` | Must keep `AllowAnonymous()` — public download |
| `src/TeleCloud.Server/Features/Files/Upload/UploadEndpoint.cs` | Already lacks `AllowAnonymous()` — already protected |
| `src/TeleCloud.Server/Infrastructure/ApiCookieAuthExtensions.cs` | Already returns 401/403 correctly per AD-7 |

### References

- [Source: epics.md#Story-1.2] — Story definition, acceptance criteria, technical notes
- [Source: SPEC.md#CAP-5] — Server-side auth enforcement via [Authorize], not UI hiding
- [Source: src/TeleCloud.Server/Program.cs] — Current API pipeline (lines 99-103)
- [Source: src/TeleCloud.Server/Infrastructure/ApiCookieAuthExtensions.cs] — 401/403 configuration

## Dev Agent Record

### Agent Model Used

deepseek-v4-flash-free (Sisyphus via bmad-dev-story)

### Debug Log References

### Completion Notes List

- ✅ Removed `AllowAnonymous()` from `CreateTempShareEndpoint.cs` (was line 16) — POST /api/temp now requires auth
- ✅ Removed `AllowAnonymous()` from `DownloadEndpoint.cs` (was line 8) — GET /api/files/{id}/download now requires auth
- ✅ Verified `GetTempShareEndpoint.cs` and `DownloadTempShareEndpoint.cs` retain `AllowAnonymous()` — public per FR-3
- ✅ Verified `UploadFileEndpoint.cs` already lacked `AllowAnonymous()` — no change needed
- ✅ Verified `ApiCookieAuthExtensions.cs` returns 401/403 — no change needed per AD-7
- ✅ TeleCloud.Web build: 0 warnings, 0 errors (no regressions)
- ⚠️ TeleCloud.Server build: compilation succeeded, exe copy blocked by pre-existing file lock (PID 300)

### File List

- `src/TeleCloud.Server/Features/TempShares/Create/CreateTempShareEndpoint.cs` (MODIFIED — removed AllowAnonymous)
- `src/TeleCloud.Server/Features/Files/Download/DownloadEndpoint.cs` (MODIFIED — removed AllowAnonymous)
- `_bmad-output/implementation-artifacts/1-2-harden-fastendpoints-endpoints.md` (MODIFIED — task checkboxes, status, Dev Agent Record)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (MODIFIED — story status updated)
