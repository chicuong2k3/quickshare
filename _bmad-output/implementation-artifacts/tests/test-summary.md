# Test Automation Summary — 2026-08-08 (branch `dev`)

> Updated after full-coverage pass (`e8cc07c`): tier 1 now 102/102.
> Added: `FolderFileApiTests` (folders CRUD, file move, avatar),
> `DeliveryExtrasApiTests` (brand, report/takedown, password verify, abort-r2, P2P share API),
> `PageCoverageTests` (/, /send, /files, /d/{code}, /p2p/d/{code}).
> More prod bugs fixed: 5 endpoints (folders CRUD, file move, avatar) had every
> response — including error statuses and bodies — replaced by an empty 204
> (raw response writes vs FastEndpoints auto-response); ExecuteUpdateAsync
> replaced with tracked updates in UpdateFolderHandler/MoveFileHandler.
> Known untested: AccountProfile.razor still uses ExecuteUpdateAsync (works on
> Postgres, would 500 under the in-memory test provider if ever covered);
> PayOS webhook valid-signature path (needs real checksum signing).

## Generated Tests

### Tier 1 — In-process E2E (WebApplicationFactory, `tests/QuickShare.Server.Tests/E2E/`)
- [x] `UploadDownloadFlowTests.cs` — full upload → share → download flow, OTP protection, expiry (410), download limit (403), unknown code (404), plan-size limit (400), auth (401), delivery listing, download counter
- [x] `PaymentFlowTests.cs` — upgrade POST creates PaymentIntent (PayOS stubbed), webhook rejects invalid signature (401), subscription activation upgrades plan
- [x] `AuthFlowTests.cs` — API 401s, logout redirect, login page, unknown OAuth provider 404
- Infra: `TestWebAppFactory` — test-auth handler + seeded user, `StubPayOSService`, warm-up request, fixed in-memory EF detection

### Tier 2 — Browser smoke (Playwright + Kestrel, `tests/QuickShare.Browser.Tests/`)
- [x] `SmokeTests.cs` — public pages render without JS errors; HTMX lazy-load swaps upgrade skeleton without full reload; P2P send generates share link in real browser
- Infra: `BrowserFixture` — `UseKestrel(0)` + headless Chromium; auth via extra HTTP header on browser context
- One-time setup: `pwsh tests/QuickShare.Browser.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`

## Real bugs found & fixed by these tests
1. **CommitR2 returned 201 with empty body** (`CreatedAtAsync` misuse) — broke `upload.js` reading `result.shareLink`. Prod upload flow was broken.
2. **Download endpoint statuses (404/410/403/302) overwritten with 204** by FastEndpoints auto-response → `DontAutoSendResponse()`.
3. **Download counter silently never incremented** — background task used the request scope after disposal.
4. Wolverine/in-memory detection: tests could not boot the host at all before (probe type name was wrong; Wolverine required Postgres).

## Results
- Tier 1: 74/74 passed (~9s)
- Tier 2: 5/5 passed (~5s)

## Commits
- `d37ee5a` tier-1 tests + bug fixes
- `1febec7` tier-2 Playwright smoke tests

## Next Steps
- Add both projects to CI (`dotnet test tests/QuickShare.Server.Tests` + playwright install + `dotnet test tests/QuickShare.Browser.Tests`)
- Optional: P2P two-context WebRTC transfer test; HTMX navigation coverage for /files
