# Tester Onboarding — QuickShare

Read this first if you are testing QuickShare. You do **not** need to read `AGENTS.md`
(that file is instructions for AI agents). Everything you need is here + `docs/backlog.md`.

## Your job in 3 questions

| Question | Answer |
|---|---|
| Which features exist / matter? | `docs/backlog.md` — tiered feature inventory |
| What can I test right now (implemented)? | `docs/TESTABLE.md` — implemented + testable snapshot |
| **What order do I test everything in?** | **`docs/test-runbook.md` — ordered manual checklist (risk-first: money → core → security → ops)** |
| What to test for one issue? | The issue's `## Manual Test Checklist` section |
| What to re-test after a change? | Regression matrix below |

## Environment

```bash
# run the app (local dev, auto-signin middleware in Program.cs)
dotnet run --project src/QuickShare

# automated suites (prove the basics before manual work)
dotnet test tests/QuickShare.Server.Tests    # ~23s, 125 tests
dotnet test tests/QuickShare.Browser.Tests   # ~31s, 15 tests (install chromium once: playwright.ps1 install)
```

Browser tests drive the **installed Chrome** via a real process (`BrowserAppFixture`) — no
browser download needed.

## Issue → test workflow

1. Open the issue. Find `## Manual Test Checklist` (this is mandatory per AGENTS.md →
   Feature Issue Testing Rule).
2. **No checklist → BLOCK the issue.** It is incomplete. Ask the developer to add it before
   you spend time testing. Do not invent your own test scope.
3. Execute every checkbox, one by one. Each step states *action → expected result*.
4. Record pass/fail with evidence:
   - UI change → screenshot
   - API/backend → response body or log line
   - otherwise → written pass/fail note
5. Large/critical features (auth, billing, payments, P2P, or 3+ modules) get a separate
   `[Manual Test] <Feature>` issue — test against that, and close it when done.

## Feature → automated coverage map

| Feature area | Automated tests |
|---|---|
| Upload / download / plan limits | `UploadDownloadFlowTests`, `UploadResumeTests`, `MonthlyQuotaTests`, `UploadResumeBrowserTests` |
| Download page gates (OTP, ad, expiry) | `DownloadPageTests`, `DownloadNoScriptTests` |
| Delivery extras (brand, abort, P2P share) | `DeliveryExtrasApiTests`, `DeliveryActivationJobTests` |
| Delivery manage (revoke #322, rename #318, .ics #232, filter/search #214, clone #218, expired archive #222, 404 #206, alias #311, daily cap #227, favorites #242) | `DeliveryManageTests` |
| Trust/retention extras (burn, SHA-256, extend, stats) | `DeliveryTrustExtrasTests` |
| Auth | `AuthFlowTests` |
| Account / profile / deliverables / upgrade | `AccountProfileTests`, `AccountDeliveriesTests`, `UpgradeFlowTests` |
| File requests (#16) — create/list/close/owner-isolation/digest | `FileRequestsApiTests`, `FileRequestDigestJobTests` |
| Notification center (#246) — list/read/unread-count/sources/retention | `NotificationCenterTests`, `NotificationJobTests` |
| Upload from URL (#57) — Pro-gate/full-flow + SSRF guard | `UploadFromUrlTests`, `IpAddressSafetyTests` |
| Extension requests (#233) — request/dedup/approve/reject/toggle | `ExtensionRequestTests` |
| Office document preview (#94) — docx/xlsx/pptx extraction + gating | `OfficeTextExtractorTests`, `OfficePreviewTests` |
| Multi-recipient share (#37) — recipient limit + normalization | `MultiRecipientTests` |
| Web push (#103) — subscribe/upsert/unsubscribe/vapid-key | `PushSubscriptionTests` |
| Privacy mode (#187) — no tracking/analytics, count hidden | `PrivacyModeTests` |
| Email OTP gate (#291) — request/cooldown/verify/single-use | `EmailOtpTests` |
| Password breach check (#280) — client-side HIBP k-anonymity | manual only (browser) |
| Account data export + deletion (#59) | `AccountDeletionTests` |
| Payment (PayOS) | `PaymentFlowTests`, `PayOSWebhookSignatureTests` |
| P2P WebRTC | `P2PSignalingRelayTests`, `P2PTransferBrowserTests`, `P2PDisconnectBrowserTests`, `P2PNetworkPreflightTests`, `P2PQuotaAndAdTests` |
| Analytics | `AnalyticsTrackingTests` |
| Sender email (confirm/unsubscribe/rate-limit) | `SenderEmailConfirmTests`, `SenderEmailUnsubscribeTests`, `SenderEmailRateLimiterTests`, `SenderEmailTokensTests` |
| Security headers + robots | `SecurityHeadersTests` |
| Trust static pages (security.txt, changelog, roadmap) | `PageCoverageTests` |
| SEO + trust (sitemap, robots, compare, why-private) | `PageCoverageTests` |
| Rate-limit headers + help center | `PageCoverageTests` |
| Rate limiting escalating backoff (#52) | `RateLimitingMiddlewareTests` |
| CSV export + feedback | `FeedbackExportTests` |
| OTP brute-force lockout | `OtpLockoutTests` |
| Share password (download gate) | `DeliveryPasswordTests` |
| New-device login tracking | `DeviceTrackerTests` |
| Abuse/DMCA reports + admin | `AbuseReportTests` |
| Localization vi/en | `LocalizationTests`, `LanguageSwitchTests` |
| HTMX lazy-load / cache / page render | `HtmxLazyLoadTests`, `HtmxCacheTests`, `PageLoadTests`, `PageCoverageTests` |
| Quick Paste | `QuickPasteTests`, `PasteBrowserTests` |
| R2 storage | `R2ObjectKeyTests`, `OrphanSweepJobTests`, `LocalDiskStorageProviderStubTests`, `R2StorageExceptionTests` |
| Smoke / no-JS-error pages | `SmokeTests` |

## Regression matrix

When code changes in an area, re-run BOTH the automated suite for that area AND the manual
checklist of the touched issue. Minimum regression set per change:

| Changed area | Re-run automated | Re-check manually |
|---|---|---|
| Upload / R2 / chunking | `UploadDownloadFlowTests`, `UploadResumeTests`, `R2ObjectKeyTests`, `OrphanSweepJobTests` | large-file upload, resume after network drop |
| Download / delivery gates | `DownloadPageTests`, `DownloadNoScriptTests`, `DeliveryExtrasApiTests` | OTP, expired, no-JS path |
| Payment / billing | `PaymentFlowTests`, `PayOSWebhookSignatureTests`, `UpgradeFlowTests` | PayOS sandbox (real money) |
| Auth / login | `AuthFlowTests` | Google OAuth (real screen) |
| P2P | `P2PSignalingRelayTests`, `P2PTransferBrowserTests`, `P2PDisconnectBrowserTests` | NAT/4G real devices |
| Analytics | `AnalyticsTrackingTests` | — |
| Localization | `LocalizationTests`, `LanguageSwitchTests` | — |
| HTMX / page render | `HtmxLazyLoadTests`, `HtmxCacheTests`, `PageLoadTests`, `SmokeTests` | — |
| Any visible UI | `SmokeTests` | drag&drop, clipboard feel |

## Manual-only (automation cannot cover — always a human)

- P2P over real NAT / 4G networks
- Real mobile Safari + Chrome (iOS/Android)
- PayOS sandbox with real money movement
- Google OAuth on the real consent screen
- Drag-and-drop / clipboard feel (UX judgment)
- Screenshot evidence for any UI change

## Reporting

- Pass/fail + evidence goes in the issue or PR `## Manual Test Evidence` section.
- A feature is **not done** until every checklist item is checked with evidence.
- Ambiguous or contradictory expected result → ask, do not guess.

---

## Keep fresh

Update this file when a test suite is added/renamed/removed or a feature's coverage changes.
See `AGENTS.md` → Issue Completion Docs Update Rule.
