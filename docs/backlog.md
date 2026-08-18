# Backlog & Feature Review Index

Single entry point to review every open issue + feature. Canonical tier list lives in
[GitHub #325](https://github.com/chicuong2k3/quickshare/issues/325) (party-mode, 2026-08-14);
this file adds **status** + **local-doc cross-reference** + **review workflow**.

New here? Start at your role's entry point:
- Developer → [`docs/DEVELOPER.md`](DEVELOPER.md)
- Tester → [`docs/TESTING.md`](TESTING.md) — feature→test map, issue→checklist, regression matrix.
- DevOps → [`docs/SETUP.md`](SETUP.md) — prerequisites, env vars, local run, deploy.
- Support / on-call → [`docs/ONCALL.md`](ONCALL.md) — incident playbooks + rollback.

## 1. How to review (the mechanism)

| Want | Command / URL |
|---|---|
| All open issues | `issue://?state=open` |
| One tier | `issue://?state=open&label=priority-tier-4` |
| One issue | `issue://<N>` |
| GitHub web filter | `https://github.com/chicuong2k3/quickshare/issues?q=is:issue+is:open+label:priority-tier-4` |
| Local full tier rationale | [issue #325](https://github.com/chicuong2k3/quickshare/issues/325) |

`issue://` lists max 100 per query (newest first). For tiers > 100 issues, filter by label.

## 2. Status snapshot (verified 2026-08-15)

| Tier | Meaning | State |
|---|---|---|
| Tier 1 | Foundation / survival risk | **10/10 done** (08-17) |
| Tier 2 | Existing risk (cheap fixes) | all open |
| Tier 3 | Growth + monetization levers | **all done** (08-16) |
| Tier 4 | WeTransfer parity baseline | 48 done (08-17): `#113` expiry countdown, `#12` burn-after-download, `#45` extend link, `#64` download stats, `#66` SHA-256, `#322` revoke, `#318` rename, `#232` expiry .ics, `#214` list filter/search, `#287` password meter, `#222` expired archive, `#218` clone, `#226` print view, `#206` branded 404, `#227` daily cap, `#311` alias, `#242` favorites, `#315` largest-files report, `#56` security.txt, `#58` changelog + roadmap, `#170` EXIF/GPS strip, `#49` range requests, `#47` success QR, `#125` audio preview, `#77` expired UX, `#78` Accept-Language, `#205` file-type icons, `#127` dark mode, `#321` delivery labels, `#138` search/filter (via #214), `#213` empty states, `#99` trash/undo delete, `#33` download tracking, `#27` upload resume, `#241` recents, `#274` rate-limit note, `#117` notification prefs, `#144` bulk manage, `#153` command palette, `#59` account deletion + data export, `#129` onboarding, `#63` auto-compress images, `#157` delete forever, `#178` text snippet sharing, `#240` markdown + code preview, `#220` download history, `#72` replace file, `#215` zip archive preview, `#29` expiry reminder email |
| Tier 5 | Growth wave 2 | 20 done: `#69` scheduled send, `#88` sitemap + robots.txt, `#168` comparison page, `#25` privacy trust page, `#97` GitHub OAuth login, `#36` status page, `#143` rate-limit headers, `#116` help center, `#74` CSV export, `#35` feedback widget, `#60` email verification (N/A), `#83` subscription management, `#87` VND pricing, `#134` abuse reporting (via #53), `#31` referral dashboard, `#283` native share sheet, `#299` plan comparison, `#202` link health, `#62` weekly digest, `#133` quota meter + upgrade nudge |
| Tier 6 | B2B / enterprise / far-future | 2 done: `#250` QR label sheet, `#282` announcement banner |

Dedup note: 2026-08-14 closed ~16 `VERIFIED gap` issues as `duplicate` (folded into #325).
Per-issue status above is from `issue://?state=closed` (newest-100 window); Tier 1 closed items
verified directly.

> ✅ Resolved 2026-08-17: `#52` (rate limiting) — base middleware + escalating backoff
> (60s → 5min → 1h lockout after repeated violations) now committed. Tier 1 fully done.

## 3. Tier 1 — Foundation (do-first)

| # | Item | Status |
|---|---|---|
| #52 | Rate limiting + abuse protection | DONE 08-17 (escalating backoff on top of existing middleware) |
| #76 | Capture sender email on anonymous upload | DONE 08-15 |
| #93 | Legal pages ToS + Privacy (+ #230 PDPD) | DONE 08-14 |
| #10 | Enforce monthly quota (was advertised-only) | DONE 08-15 |
| #24 | Analytics instrumentation | DONE 08-15 |
| #90 | Automated DB + R2 backup/restore runbook | DONE 08-15 |
| #89 | Error tracking + alerting | DONE 08-15 |
| #107 | Orphan R2 object sweep | DONE 08-14 |
| #96 | CI/CD pipeline | DONE 08-14 |
| #91 | Feature flags | DONE 08-15 |

## 4. Tier 2 — Existing risk (cheap, bleeding slowly)

All **done** (08-16): #121 (noindex/nofollow — X-Robots-Tag + robots meta on /d/*),
#82 (security headers + CSP — config-driven SecurityHeadersMiddleware),
#148 (account lockout — 5 fails → 15min/1h/24h escalating, 423 + Retry-After),
#149 (new-device alert — UA+IP-hash fingerprint, alert email, /account/devices management),
#175 (download CAPTCHA — Cloudflare Turnstile after repeated OTP failures),
#53 (report abuse + DMCA — anonymous report on /d/*, admin inbox, suspend, /abuse policy page).

## 5. Tier 3 — Growth + monetization levers

All **done** (08-16): #75 (OpenGraph cards), #65 (7-day Pro trial), #15 (viral CTA + referral
+2GB), #28 (SEO landing pages), #21 (file previews), #38 (native share sheet + QR),
#22 (free-tier 24h/7d expiry), #14 (PWA + Android Share Target), #44 (password-protected
downloads), #137 (message with delivery).

## 6–8. Tier 4 / 5 / 6

Full item tables: [issue #325](https://github.com/chicuong2k3/quickshare/issues/325).
Everything listed there is open except the items marked done above. Sizes: Tier 4 ≈ 70 items,
Tier 5 ≈ 90 items, Tier 6 ≈ 50 items.

Tier 4 batch 2026-08-16 (commit `5d0ca8d`, tests in `DeliveryManageTests`):
`#322` revoke link before expiry, `#318` rename after upload, `#232` expiry as .ics calendar
event, `#214` delivery list status filter + name search, `#287` password strength meter.
Tier 4 batch 2, same day (commit `e7acec0`): `#222` expired archive (30-day
`ExpiredRowRetention` in `DeliveryCleanupService`; archive UI = the Expired filter),
`#218` clone (`IStorageProvider.CopyObjectAsync` + `ShareCodes.Generate()`),
`#226` print view (`@media print` in `app.css` — rides with the uncommitted theme rewrite),
`#206` branded 404 (catch-all `NotFound.razor`).

Tier 4 batch 3, same day (commit `8bba30f`, migration `AddDeliveryAliasFavoriteDailyLimit`):
`#227` per-link daily download cap (Pro; enforced at the download choke point via
DownloadTracking counts), `#311` custom alias (Pro; unique lowercase slug, `ByCode()`
resolves code-or-alias on every recipient path), `#242` favorites (star + filter),
`#315` largest-files report on the usage card. Batch-3 CSS also rides in the
uncommitted `app.css`.

Tier 4 batch 4, 2026-08-17: `#248` code diff preview (`.patch`/`.diff` files render as a
unified/split diff in the download text preview — `parseDiff` + `renderDiff` in
`download.ts`, `+/-` markers + line numbers, raw fallback, copy).

Tier 4 batch 5, 2026-08-17: `#16` file request — owner-side half done: `FileRequest`
entity + `UploadSession.FileRequestId`, create/list/close/get endpoints
(`/api/file-requests`), the `/account/requests` tab + lazy-loaded page
(`AccountFileRequests.razor`), and `FileRequestDigestJob` (Hangfire, `DigestNotifiedAt`
on `Delivery`, migration `AddDeliveryDigestNotifiedAt`). **Blocked remainder**: the
`/r/{code}` submission page + anonymous upload wiring — paused on the E2E-encryption
decision (key-in-URL-fragment) for request uploads.

Tier 4 batch 6, 2026-08-17: `#246` in-app notification center — `Notification` entity
+ migration `AddNotifications`, list/mark-read/read-all/unread-count endpoints
(`/api/notifications`), `/account/notifications/inbox` page (filter + mark-read + infinite
scroll), navbar bell + unread badge, `NotificationCleanupJob` (30-day retention). Sources:
delivery expired (`DeliveryExpiryJob`) + downloaded (`DownloadNotificationJob`).

Tier 4 batch 7, 2026-08-17: `#57` upload from URL — Pro/Studio server-side remote fetch
(`POST /api/upload-from-url`) into R2 as a delivery. SSRF-guarded (`IpAddressSafety` blocks
private/loopback/link-local/CGNAT/reserved ranges at connect time via `RemoteUrlFetcher`),
size-capped streaming, quota-checked. Send page "Upload from URL" input for paid plans.

Tier 4 batch 8, 2026-08-17: `#233` recipient extension request — anonymous recipient asks
for more time on an expired delivery (`POST /api/deliveries/{code}/extension-request`),
one per recipient, sender opt-in toggle (`AllowExtensionRequests`, default off). Sender
gets in-app + email notification and approves (extends) or rejects from
`/account/deliveries/{id}/extension-requests`. Expired page shows "Request more time" when enabled.

Tier 4 batch 9, 2026-08-17: `#94` office document preview — docx/xlsx/pptx render
server-extracted, HTML-escaped text (no macros executed) via `OfficeTextExtractor`
(ZipArchive + LINQ-to-XML, no new dependency). `GET /api/downloads/{code}/office-preview`,
20MB cap, gated like the media preview. Download page preview tab for office types.

Tier 4 batch 10, 2026-08-17: `#37` multi-recipient share — "send link to" field at upload
emails the link to N recipients (`CommitR2.RecipientEmails`), capped per plan (Free 3,
Pro/Studio 20), deduped + validated server-side.

Tier 4 batch 11, 2026-08-17: `#103` web push notifications — `PushSubscription` entity,
VAPID auth via `Lib.Net.Http.WebPush`, subscribe/unsubscribe/vapid-key endpoints,
`sw.js` push + notificationclick handlers, opt-in button on the notification center,
and a `DownloadNotificationJob` push trigger (mirrors the email + in-app notification).

Tier 5 batch 1, 2026-08-17: `#187` per-delivery privacy mode — `Delivery.PrivacyMode` toggle
at upload: no DownloadTracking write, no analytics event, count hidden on the download page
("Privacy mode: no tracking" badge). Max-downloads/burn enforcement still works via the
internal counter; expiry/password/OTP gates unchanged.

Tier 5 batch 2, 2026-08-17: `#291` email OTP gate — recipient requests a single-use code
emailed to them (`POST /api/deliveries/{code}/request-email-otp`, BCrypt-hashed, 15-min TTL,
60s cooldown). Download verifies the code (`?emailOtp=&email=`) with a 10-min retry grace,
then marks it used. Upload toggle "Require emailed code".

Tier 5 batch 3, 2026-08-17: `#280` password breach check — client-side k-anonymity against
the Have I Been Pwned range API (SHA-1 prefix only, never the full hash) with a debounced
inline warning + "generate strong password" helper. Fail-open when the API is unreachable.
Client-side only; manual browser test (no automated C# coverage).

Tier 5 batch 4, 2026-08-18: `#324` recipient email gate — anonymous downloaders surrender an
email (stored on `DownloadTracking` with a marketing-consent flag) before download, gated by
`Delivery.RequireRecipientEmail`. Upload toggle + download-page gate.

Tier 5 batch 5, 2026-08-18: `#294` public delivery stats — opt-in `Delivery.PublicStatsEnabled`
upload toggle exposes an aggregated stats page at `/d/{code}/stats` (total downloads, unique
visitors via distinct IP, 7-day daily timeline, first-downloaded date; never per-recipient
identity) behind `PublicStatsHandler` + anonymous `GET /api/deliveries/{code}/public-stats`
(30s cache). Privacy-mode deliveries never serve stats; any failure renders the same generic
"not available" state so the page leaks nothing.

Tier 5 batch 6, 2026-08-18: `#126` account security activity — `SecurityEvent` entity (append-only
audit trail, raw IP never stored — SHA-256 hash only, privacy #26) + `SecurityEventType` enum.
Wired events today: login + new-device (`FinalizeOAuthAsync`) and session revoke
(`RemoveDeviceEndpoint`). `/account/security/activity` timeline card on the account page with
query-string filter (all/sign-ins/new devices/revoked). `SecurityEventCleanupJob` (Hangfire,
90-day retention). Events ride the GDPR export. Migration `AddSecurityEvents`.

Tier 5 batch 7, 2026-08-18: `#316` recent recipients + address book — `RecipientContact`
entity (unique per user+email, `UseCount` + `LastUsedAt`). Commit-time capture
(`CommitR2Handler.CaptureRecipientsAsync`) remembers who a signed-in user sent to — never in
privacy mode, never anonymous, best-effort. Address-book CRUD via
`GET/POST /api/me/recipients`, `DELETE /api/me/recipients/{id}` (30s cache), upload page
quick-pick chips (auth only), GDPR export carries the list. Migration `AddRecipientContacts`.

## 9. Local artifact map (feature area → design doc)

Where a design/spec/plan already exists, read it before implementing.

| Feature area | Local docs |
|---|---|
| Upload / R2 storage | `_bmad-output/specs/spec-r2-storage/`, `_bmad-output/implementation-artifacts/1-1-r2-infrastructure.md`, `1-2-chunked-upload-api.md`, `US-CAP-1`, `US-CAP-2` |
| E2E encryption | `1-4-e2e-encryption.md`, `US-CAP-2` |
| Recipient download | `1-3-recipient-download.md`, `US-CAP-3`, `US-CAP-4` |
| P2P WebRTC | `_bmad-output/specs/spec-webrtc-p2p/`, `docs/p2p-architecture.md`, `docs/superpowers/plans/2026-07-26-p2p-multi-receiver-plan.md` |
| Payment / subscription | `_bmad-output/specs/spec-payment/`, `docs/payment-architecture.md`, `US-CAP-8` |
| Accounts / plans / quota | `1-6-accounts-plans.md`, `US-CAP-6`, `US-CAP-9` |
| Legal pages | `_bmad-output/specs/spec-legal-pages/` |
| Localization | `_bmad-output/specs/spec-localization-completeness/` |
| Ad monetization | `_bmad-output/specs/spec-ad-monetization/` |
| Pricing page | `_bmad-output/specs/spec-pricing-page/` |
| Login / auth | `docs/login-architecture.md`, `_bmad-output/specs/spec-enforce-login/` |
| Sender email capture | `docs/superpowers/specs/2026-08-15-sender-email-capture-design.md` |
| Analytics | `docs/analytics-architecture.md` |
| Observability | `docs/observability-setup.md`, `docs/observability-theory.md` |
| Send flow | `docs/send-architecture.md` |
| Profile | `docs/profile-architecture.md` |
| Style gallery | `docs/superpowers/specs/2026-08-15-quickshare-style-gallery-design.md` |
| HTMXor integration | `docs/superpowers/plans/2026-07-25-htmxor-integration-plan.md` |

## 10. Deferred work + test backlog

- Deferred (code-review findings, 2026-07-19): `_bmad-output/implementation-artifacts/deferred-work.md`
  — includes `LocalDiskStorageProvider` full-chunk read, `Delivery.CreatedAt` index,
  `UploadSession.Status` string→enum, orphan-sweep streaming.
- Manual test checklists (#2–#5, #9, #20) = test backlog, not product work (per #325).

## 11. Review checklist

- [x] Tier 1: implement `#52` rate limiting → Tier 1 fully done (08-17)
- [x] Tier 2: all 6 done 08-16 (see section 4)
- [x] Tier 3: all 10 done 08-16 (see section 5)
- [ ] Tiers 4–6: use label filter per tier; do NOT work them until Tier 2/3 clear
