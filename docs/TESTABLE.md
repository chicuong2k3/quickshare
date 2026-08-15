# Testable Now — Implemented Features & Issues

Snapshot: **2026-08-16**. Everything listed here is **implemented and testable today**,
mapped to GitHub issues + automated coverage. If a feature is not on this list, it is a
backlog item, not implemented — see [`docs/backlog.md`](backlog.md).

## Status legend

| Marker | Meaning |
|---|---|
| ✅ committed | merged to git `HEAD` — test from a clean checkout |
| ⚠️ working-tree | implemented locally but **NOT committed** (uncommitted diff in your checkout) |

> The whole **Security & Trust batch** (section 4) is committed to `dev` as of 2026-08-16
> (commits `fab6003` + `d702fcf`). It builds clean and ships its own E2E tests. The
> ordered manual checklist lives in [`docs/test-runbook.md`](test-runbook.md).

## 0. Run the app

```bash
# local dev (auto-signin middleware in Program.cs — no Google screen needed)
dotnet run --project src/QuickShare        # → http://localhost:9000

# automated suites
dotnet test tests/QuickShare.Server.Tests    # in-process (~23s, 125 tests + new E2E)
dotnet test tests/QuickShare.Browser.Tests   # Playwright on installed Chrome (~31s, 15 tests)
```

## 1. Core sharing — ✅ committed

| Feature | Issue | How to test | Automated coverage |
|---|---|---|---|
| Chunked upload → R2 (any size) | core | `/send` → drop a large file → progress, resume after network drop | `UploadDownloadFlowTests`, `UploadResumeTests`, `R2ObjectKeyTests`, `LocalDiskStorageProviderStubTests` |
| End-to-end AES-256-GCM encryption (key in URL fragment) | core | upload → open link in another browser → download, verify bytes | `UploadDownloadFlowTests` |
| Download page gates: OTP, ad-reward, expiry | core | open `/d/{code}` → wrong OTP, expired link, no-JS path | `DownloadPageTests`, `DownloadNoScriptTests` |
| Delivery extras: brand page, abort, P2P share, message | core | `/account/deliveries` → manage a delivery | `DeliveryExtrasApiTests` |
| Scheduled delivery (Pro) | #69 | schedule `DeliverAt` → link activates at the chosen time | `DeliveryActivationJobTests` |
| Expiry countdown on delivery page | #113 | open a delivery → live countdown → threshold states | `DownloadPageTests` |
| Sender email capture on anonymous upload | #76 | `/send` → optional email → confirm link email → unsubscribe link → rate-limited resend | `SenderEmailConfirmTests`, `SenderEmailUnsubscribeTests`, `SenderEmailRateLimiterTests`, `SenderEmailTokensTests` |
| Quick Paste (image/text → link) | #13 | paste an image or text → get a link | `QuickPasteTests`, `PasteBrowserTests` |
| Monthly quota enforced | #10 | upload past free quota → blocked / upgrade prompt | `MonthlyQuotaTests`, `UpgradeFlowTests` |

## 2. Accounts, billing, P2P — ✅ committed

| Feature | Issue | How to test | Automated coverage |
|---|---|---|---|
| Google login | (manual test #19 closed) | `/login` → Google consent screen (real OAuth, manual) | `AuthFlowTests` |
| Account pages: profile, deliveries, billing | #6 (manual test closed) | `/account`, `/account/profile`, `/account/deliveries`, `/account/billing` | `AccountProfileTests`, `AccountDeliveriesTests` |
| Avatar storage on R2 public objects | (manual test **#326 OPEN**) | upload avatar → appears on public R2 URL | — |
| PayOS payments + webhook + upgrade | #7 (manual test closed) | PayOS **sandbox** → subscribe / upgrade / webhook callback | `PaymentFlowTests`, `PayOSWebhookSignatureTests`, `UpgradeFlowTests` |
| P2P WebRTC transfer (multi-receiver) | core | `/send` → share via P2P → `/p2p/d/{code}`; real NAT/4G + mobile Safari/Chrome are manual-only | `P2PSignalingRelayTests`, `P2PTransferBrowserTests`, `P2PDisconnectBrowserTests`, `P2PNetworkPreflightTests`, `P2PQuotaAndAdTests` |

## 3. Analytics, ops, trust — ✅ committed

| Feature | Issue | How to test | Automated coverage |
|---|---|---|---|
| Anonymous analytics + founder dashboard | #24 | do uploads/downloads → `/admin/analytics` shows events (no PII) | `AnalyticsTrackingTests` |
| Feature flags + admin flags page | #91 | `/admin/flags` → toggle a flag → behavior changes | `FeatureFlagServiceTests`, `AdminFlagsApiTests` |
| Legal pages ToS + Privacy (+ PDPD) | #93 | footer links → `/legal/…` pages render vi/en | `PageCoverageTests`, `LocalizationTests` |
| Error alerting + health checks | #89 | trigger an error → alert (Seq/Sentry); `/health` | `SmokeTests` |
| DB + R2 backup / restore runbook | #90 | follow `docs/backup-restore-runbook.md` | — |
| Orphan R2 object sweep | #107 | failed upload → sweep job reclaims storage | `OrphanSweepJobTests` |
| CI/CD pipeline | #96 | push → GitHub Actions build + test + deploy | — |
| Localization vi/en + language switch | core | toggle language → all strings swap | `LocalizationTests`, `LanguageSwitchTests` |
| HTMX lazy-load shell + cache | core | open any page → skeleton instant, data fills | `HtmxLazyLoadTests`, `HtmxCacheTests`, `PageLoadTests` |
| Global API rate limiting | #52 | hammer `/api` → 429 + `Retry-After` | — |

## 4. Security & Trust batch — ✅ committed

Everything here maps to Tier 2 backlog items and was built together (issues #53 #82 #121
#148 #149 #175, closed 2026-08-16). Each area ships its own E2E test. Manual steps:
[`docs/test-runbook.md`](test-runbook.md) Group C.

| Feature | Issue | How to test | Automated coverage |
|---|---|---|---|
| Report abuse / DMCA (anonymous) | #53 | download page → "Report abuse" link → `/abuse` form → admin sees it at `/admin/reports` → resolve | `AbuseReportTests` |
| Security headers + config-driven CSP/HSTS | #82 | inspect response headers on any page (CSP, HSTS, X-Content-Type-Options, …) | `SecurityHeadersTests` |
| Noindex/nofollow on private delivery pages | #121 | view source of `/d/{code}` → robots meta noindex | `SecurityHeadersTests` |
| OTP brute-force lockout | #148 | wrong OTP repeatedly → lockout kicks in (timing-safe, per-delivery) | `OtpLockoutTests` |
| New-device login alert + known devices | #149 | log in from a new UA/IP → alert email; manage devices at `/account/devices` | `DeviceTrackerTests` |
| Download CAPTCHA (Cloudflare Turnstile) | #175 | N failed OTP attempts → Turnstile challenge on verify; **without keys configured it always passes** (safe default) | — |

## 5. Needs external services / config

Some features only show their full behavior when the backing service is configured
(see `docs/SETUP.md` for env vars):

| Feature | Needs | Without it |
|---|---|---|
| Upload/download to object storage | Cloudflare R2 (or local disk stub) | app uses `LocalDiskStorageProvider` for dev |
| Google login | Google OAuth client | auto-signin dev middleware covers local dev |
| PayOS payment | PayOS sandbox keys | upgrade flow can't complete end-to-end |
| Sender email / new-device alert emails | SMTP creds | emails won't send (flows log/rate-limit instead) |
| Turnstile CAPTCHA (#175) | Cloudflare Turnstile site+secret keys | CAPTCHA always passes — still safe default |
| Alerting / observability (#89) | Seq (or Loki/Sentry) | events still logged, no push alert |
| Backup to R2 (#90) | R2 bucket for backups | runbook scripts fail at upload step |

## 6. Notes / staleness

- `docs/backlog.md` status snapshot says "verified 2026-08-15" and lists **#52 open** and
  **Tier 2 all open**. Both are stale: rate limiting is committed and wired in
  `Program.cs`, and the Tier 2 batch was implemented and closed 2026-08-16 (see
  `docs/test-runbook.md`). `docs/backlog.md` §4 has been updated to reflect Tier 2 done.
- Manual test checklist lives in each feature issue (`## Manual Test Checklist`). No
  checklist → the issue is not done (see `AGENTS.md` → Feature Issue Testing Rule).
- Keep this file fresh when features ship: see `AGENTS.md` → Issue Completion Docs
  Update Rule.
