# Test Automation Summary — E2E coverage for GitHub issues #2–#9

Date: 2026-08-09 · Framework: xUnit + `TestWebAppFactory` (in-process, R2/PayOS stubbed, EF InMemory)
Result: **115/115 passing** (`dotnet test tests/QuickShare.Server.Tests`)

## Generated Tests

### New files
- [x] `tests/QuickShare.Server.Tests/E2E/LocalizationTests.cs` — issue #9: vi culture cookie renders Vietnamese (home + pricing), default renders English, no raw resource keys leak. Non-ASCII is HTML-entity-encoded by Razor, so assertions decode first; culture cookie sent via `HandleCookies=false` client (the default cookie container drops manual `Cookie` headers).
- [x] `tests/QuickShare.Server.Tests/E2E/AccountProfileTests.cs` — issue #6: profile form round-trip through the full Htmxor protocol (`hxor-eventid` → `HXOR-Event-Handler-Id` header, `HX-XSRF-TOKEN` cookie → `RequestVerificationToken` header) + unauthenticated redirect to /login.

### Added to existing files
- [x] `PaymentFlowTests.UpgradePost_WithoutWebhook_PlanStaysFree_IntentStaysPending` — issue #7 cancel path: no webhook → plan stays free, intent stays Pending.
- [x] `UploadDownloadFlowTests.StartUpload_ProPlan_6GB_Succeeds` — issue #7: Pro user starts a 6GB upload (246×25MB parts) successfully.
- [x] `DeliveryExtrasApiTests.SetBrand_ProUser_Succeeds_AndShowsOnBrandEndpoint` — issue #9: Pro user sets branding; anonymous GET of the brand endpoint returns it.

## Coverage vs issues
| Issue | Automated (pre-existing + new) | Still manual-only |
|---|---|---|
| #2 anonymous upload | full API flow, 100MB cap, OTP | drag&drop, clipboard paste, QR |
| #3 large upload/resume | part-URL refresh, abort, commit guards | real network loss/wifi toggle |
| #4 download gates | OTP, 410 expiry, 403 limit, 404 | ad countdown UX; **Report endpoint** (feature currently absent from `src`) |
| #5 P2P | share create, quotas, ad reward | real WebRTC transfer, NAT |
| #6 login/account | auth 401s, logout, profile form round-trip, deliveries list | real Google OAuth |
| #7 PayOS | intent creation, bad signature 401, activation, cancel path, Pro 6GB | real sandbox payment + webhook signature |
| #8 files page | — (feature removed, issue closed) | — |
| #9 branding/i18n | free 403, pro set+get brand, vi/en rendering, raw-key leak check | mobile devices, per-language layout |

## Known limitations
- Profile rename persistence can't be asserted: the page saves via `ExecuteUpdateAsync`, unsupported by the EF InMemory provider. The test covers the validation path (proves Htmxor dispatch + antiforgery + handler execution). Full persistence needs a SQLite in-memory test double.

## Next Steps
- Swap the factory to SQLite in-memory if `ExecuteUpdateAsync` paths grow.
- Playwright (`tests/QuickShare.Browser.Tests`) is the place for drag&drop/clipboard/WebRTC flows if those ever need automation.
