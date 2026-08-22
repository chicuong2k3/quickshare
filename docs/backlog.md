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
| Tier 4 | WeTransfer parity baseline | 60 done (08-23) — **tier complete**: `#275` manual folder order, `#230` PDPD consent, `#16` file-request submission page, `#17` drop room, `#104` upload by email, `#95` a11y pass, `#37` multi-recipient tracking, `#154` collection per-item stats, `#34` recipient re-request (via #233), `#244` move/copy between folders, `#284` multi-select move, `#221` per-folder storage, `#113` expiry countdown, `#12` burn-after-download, `#45` extend link, `#64` download stats, `#66` SHA-256, `#322` revoke, `#318` rename, `#232` expiry .ics, `#214` list filter/search, `#287` password meter, `#222` expired archive, `#218` clone, `#226` print view, `#206` branded 404, `#227` daily cap, `#311` alias, `#242` favorites, `#315` largest-files report, `#56` security.txt, `#58` changelog + roadmap, `#170` EXIF/GPS strip, `#49` range requests, `#47` success QR, `#125` audio preview, `#77` expired UX, `#78` Accept-Language, `#205` file-type icons, `#127` dark mode, `#321` delivery labels, `#138` search/filter (via #214), `#213` empty states, `#99` trash/undo delete, `#33` download tracking, `#27` upload resume, `#241` recents, `#274` rate-limit note, `#117` notification prefs, `#144` bulk manage, `#153` command palette, `#59` account deletion + data export, `#129` onboarding, `#63` auto-compress images, `#157` delete forever, `#178` text snippet sharing, `#240` markdown + code preview, `#220` download history, `#72` replace file, `#215` zip archive preview, `#29` expiry reminder email |
| Tier 5 | Growth wave 2 | 20 done: `#69` scheduled send, `#88` sitemap + robots.txt, `#168` comparison page, `#25` privacy trust page, `#97` GitHub OAuth login, `#36` status page, `#143` rate-limit headers, `#116` help center, `#74` CSV export, `#35` feedback widget, `#60` email verification (N/A), `#83` subscription management, `#87` VND pricing, `#134` abuse reporting (via #53), `#31` referral dashboard, `#283` native share sheet, `#299` plan comparison, `#202` link health, `#62` weekly digest, `#133` quota meter + upgrade nudge |
| Tier 6 | B2B / enterprise / far-future | 40 done (08-22): `#194` Outlook add-in, `#243` OCR search, `#225` crypto payments, `#277` voice commands, `#265` S3 gateway, `#195` WebDAV mount, `#81` white-label custom domain, `#155` preview watermark, `#289` delivery comments, `#180` Slack/Teams posting, `#162` approval workflow, `#176` recurring delivery, `#119` data residency, `#285` branded recipient page, `#286` custom fonts, `#171` S3-compatible storage, `#271` workspace backup, `#190` tenant isolation, `#191` SIEM streaming, `#184` self-serve DPA, `#147` SOC 2 readiness, `#323` SOC 2 roadmap, `#250` QR label sheet, `#282` announcement banner, `#308` file lock/checkout, `#266` legal hold, `#161` workspace retention policy, `#142` delivery IP allowlist, `#166` geo-restriction, `#106` team workspaces, `#252` ownership transfer, `#145` workspace audit log, `#300` admin usage report, `#151` shared folders, `#245` per-folder defaults, `#234` folder templates, `#278` team seats, `#141` Studio overage, `#112` e-invoice, `#192` PO + net terms |

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

Tier 6 batch 1, 2026-08-19: `#308` file lock/checkout — `FileLock` entity (checkout lock on a
delivery; active while `ReleasedAt == null` and `ExpiresAt` in future). Sender locks via
`POST /api/deliveries/{id}/lock` (optional `DurationMinutes` 1–10080, default 24h, `Reason`
≤200 chars), force-break any foreign lock (notifying the holder), unlocks via
`POST /api/deliveries/{id}/unlock`. Replacement while another user holds the lock →
`423` (`delivery_locked`), holder exempt. Lock state enriches the delivery list
(`DeliveryItem.Lock`). `FileLockExpiryJob` (Hangfire minutely) auto-releases expired locks.
Lock/force-break/expire events ride the account activity trail (SecurityEvent) and analytics.
Migration `AddFileLock`.

Tier 6 batch 2 (compliance series), 2026-08-20: `#266` legal hold, `#161` workspace retention
policy, `#142` delivery IP allowlist, `#166` geo-restriction — one migration
`AddLegalHoldAndScopeRulesAndRetention` (3 legal-hold + 3 scope-rule columns on `deliveries`,
2 retention columns on `users`, `IX_deliveries_LegalHoldAt`). Tests: `LegalHoldTests` (15),
`DeliveryAccessScopeTests` (10), `DeliveryAccessPolicyTests`, `RetentionPolicyTests`,
`RetentionPolicyApiTests` (19). Full suite after the batch: 561 server + 31 browser.

`#266` legal hold — `Delivery.LegalHoldAt` / `LegalHoldReason` (max 200) / `LegalHoldById`;
sender-only `POST /api/deliveries/{id}/legal-hold`, `.../legal-hold/lift`, and a counsel CSV at
`GET /api/deliveries/legal-hold/export`. A hold never expires on its own — a lapsed hold is
evidence loss mid-litigation — so there is no expiry field and no sweeper that clears one; only the
owner lifts. Every deletion path therefore checks it: trash, purge, bulk (row counted in
`skipped`), account deletion (all-or-nothing 409), `DeliveryExpiryJob` (link expires, R2 objects
survive), both `DeliveryCleanupService` sweeps, and the `#161` retention sweep. Errors
`legal_hold_active`, `legal_hold_not_active`, `legal_hold_reason_required`; audited as
`SecurityEventType.LegalHoldPlaced` / `LegalHoldLifted`. Design:
[`docs/compliance-architecture.md`](compliance-architecture.md).

`#161` workspace retention policy — `User.RetentionDays` (null = no policy) + `RetentionNoExtend`,
Pro/Studio only, window 1–365 days (`Infrastructure/RetentionPolicy.cs`, presets 1/7/14/30/90).
The policy is a ceiling, never a floor: a sender's shorter choice always survives and clamping is
not a validation error, so every write site applies the plan cap first and the policy clamp second
(commit, extend, approve-extension, plus `DeliveryExpiryJob.EnforceRetentionAsync` to converge rows
created before the policy existed). `RetentionNoExtend` turns both extend paths into refusals
(`retention_policy_locked`) against the *delivery owner's* window, not the caller's — a recipient
can ask, but the owner's policy answers. `PUT /api/account/retention` sets it;
`GET /api/account/retention/preview?days=N` is a read-only dry-run (affected count + at most 20
samples, 30s cache). Errors `retention_days_invalid`, `retention_policy_locked`. Design:
[`docs/compliance-architecture.md`](compliance-architecture.md).

`#142` delivery IP allowlist — `Delivery.AllowedIpRanges` (comma-separated CIDR or bare address,
max 500 chars) evaluated by pure `Infrastructure/DeliveryAccessPolicy.cs`; caller IP is
`Connection.RemoteIpAddress`, already rewritten by `UseForwardedHeaders`. Fail-closed: a rule that
exists refuses anyone it does not explicitly satisfy, and a malformed rule counts as "no match",
never "matches everything" — a typo must not silently open a restricted link. One scoped
`DeliveryAccessService` carries the check for every recipient-facing path (info, download, preview,
office preview, zip contents) so the page, the byte stream and each preview cannot disagree; its
30s cache holds the rule snapshot only, because a per-caller verdict must never enter a shared
cache. Refusals return `ErrorCodes.DeliveryRestricted` and render `#download-phase-restricted`,
which leaks no file name, size or download count and stays `noindex, nofollow`; blocked attempts
are audited on the *sender's* timeline (`SecurityEventType.DeliveryAccessBlocked`) because the
recipient is anonymous and the sender is who needs to see rejects. Design:
[`docs/compliance-architecture.md`](compliance-architecture.md).

`#166` geo-restriction — `Delivery.AllowedCountries` / `BlockedCountries` (same 500-char limit),
country from the Cloudflare `CF-IPCountry` edge header, decision `CountryNotAllowed`. Shares the
primitive, the service, the restricted panel and the audit trail with `#142`; rule sets are
AND-scoped, so a caller must satisfy every configured rule, and rules left unset refuse nobody.
Both features are Pro/Studio, but normalisation runs before the gate: an all-garbage list collapses
to null, which is "no rule" and must never trip `requires_paid_plan`, so free senders send three
nulls and never meet the gate. Design:
[`docs/compliance-architecture.md`](compliance-architecture.md).


Tier 6 batch 3 (team series), 2026-08-21: `#106` team workspaces, `#252` ownership transfer,
`#145` workspace audit log, `#300` admin usage report — one migration `AddTeamWorkspaces`
(3 new tables `workspaces` / `workspace_members` / `workspace_transfers`, plus
`users.PersonalPlan`, `deliveries.WorkspaceId`, `security_events.WorkspaceId`). Tests:
`WorkspaceTests` (18). Design: [`docs/workspaces-architecture.md`](workspaces-architecture.md).

`#106` team workspaces — a workspace is one bill, one pooled monthly quota and one shared
delivery history; everything else exists to keep those three true without a second source of
truth. The owner holds a real `workspace_members` row (`Role = Owner`), so seat counts, audit,
report and transfer all read one table. A pending invite holds its seat, or two admins racing
for the last slot both win. Revoked seats are kept with `RevokedAt` set — the audit log needs
its subject — and every seat query filters `RevokedAt == null`, so the slot frees immediately.
Plan inheritance is deliberate denormalization: an active seat copies the owner's plan onto
`User.Plan` and parks the member's own in `User.PersonalPlan`, which makes ~20 existing
`user.Plan` gates correct with no call-site changes; the copy is refreshed from
`SyncMemberPlansAsync` (accept/transfer) and `PushPlanToWorkspaceAsync` (all three plan writes
in `SubscriptionService`), and reversed by `RestorePersonalPlanAsync` on revoke — restoring,
not downgrading, so a member who pays for Pro personally keeps it. A filtered unique index on
`workspace_members.UserId` allows one live seat per account, because two would give
`User.Plan` two masters. Pooling lives inside `QuotaService.ResolveBucketAsync`, used by both
`CheckAsync` and `RecordAsync` — a member who checked against the pool but recorded against a
private row would have an unlimited quota. Shared history is read-only: `CommitR2Handler`
stamps `Delivery.WorkspaceId`, `ListDeliveriesHandler` widens its filter, every mutation path
still matches on `SenderId`. Routes `POST/GET/PATCH /api/workspace`,
`POST /api/workspace/invites{,/accept}`, `PATCH|DELETE /api/workspace/members/{id}`; errors
`workspace_*`; UI `/account/workspace` + `/workspace/join`.

`#252` ownership transfer — the owner nominates an active member, and nothing moves until the
successor accepts inside 7 days. Ownership carries billing, so consent is required: a workspace
whose owner vanishes is the problem, but inheriting an invoice by surprise is not the cure.
One open transfer at a time (two live tokens race and the second demotes an already-demoted
owner). The successor qualifies on `PersonalPlan ?? Plan` — the seat plan they are wearing is
the one about to disappear. The source owner is demoted to Admin rather than removed, so the
workspace is never left unadministered. Assets stay with the workspace. Account deletion is
refused while you still own one (`workspace_transfer_required` → 409) instead of crashing on the
`Restrict` FK. Routes `POST /api/workspace/transfer{,/accept,/cancel}`; tokens SHA-256 at rest.

`#145` workspace audit log — the same append-only `security_events` store as the personal
timeline (`#126`), stamped with `WorkspaceId`, so the admin lens is a filter and not a second
write path that could drift. Eleven workspace event types; filters (member, action, date) apply
in SQL; `GET /api/workspace/audit` + `/export` (CSV) share one admin gate
(`WorkspaceAuditReader.ResolveAdminSeatAsync`) so JSON and CSV cannot disagree. Retention is the
existing 90-day `SecurityEventCleanupJob`. UI `/account/workspace/audit`, filters in the URL.

`#300` admin usage report — `WorkspaceReportService.BuildAsync` is the single query set behind
the on-demand view, the CSV export and the weekly `WorkspaceReportJob` email; a second
implementation would be a second set of numbers. Rolling window (default 30d) for activity, the
current billing month for quota. Workspace deliveries only — personal shares by the same
accounts stay out (`#187`). Reading the report is itself audited
(`WorkspaceReportGenerated`). Not shipped: PDF export (CSV only) and per-workspace report
scheduling — weekly is the only cadence, and the on-demand endpoint covers every other window.


Tier 6 batch 4 (folder series), 2026-08-21: `#151` shared workspace folders, `#245` per-folder
default settings, `#234` folder templates — one migration `AddWorkspaceFolders` (2 new tables
`workspace_folders` / `workspace_folder_templates` plus `deliveries.FolderId`). Tests:
`WorkspaceFolderTests` (19). Design: [`docs/workspaces-architecture.md`](workspaces-architecture.md).

`#151` shared folders — content organization on top of `#106`. Every seat sees the whole tree;
per-folder membership would be a second permission system layered on the workspace roles that
already exist, and two systems disagreeing is how someone gets locked out of a folder they can
see in a list. Management (create/rename/delete/defaults) needs `CanAdminister`; browsing and
uploading do not. Nesting stops at two levels — arbitrary depth needs breadcrumbs, move
semantics, cycle checks and a recursive permission walk, and "client / project" is the shape
agencies actually asked for. Deleting a folder never deletes files: refused while any delivery
(soft-deleted rows included, since `#99` keeps them restorable for 24h) or child folder is still
filed under it, with a `Restrict` FK from `deliveries.FolderId` as the database backstop.
Sibling names are unique per parent. `GET /api/deliveries?folder={id}` filters the shared
history; the folder list carries per-folder file count and bytes (live rows only). Routes
`GET/POST /api/workspace/folders`, `PATCH|DELETE /api/workspace/folders/{id}`; errors
`folder_*`; UI `/account/workspace/folders`. **Not shipped: public folder link / folder QR** —
a new recipient-facing surface needs its own access-control design, not a footnote to CRUD.

`#245` per-folder defaults — expiry hours, password-required + hint, privacy mode, and the lock.
The whole feature is the difference between two of them: an **unlocked** default is a prefill
served to the upload form (`GET /api/workspace/folders/{id}/prefill`) and the sender stays in
control, because a default that silently overrides what the sender typed is policy wearing a
friendlier word; a **locked** default is enforced in `CommitR2Handler` — expiry replaced,
privacy forced on, and a commit without a password refused (`folder_password_required`) rather
than silently generating one nobody can be told. Inheritance is nearest-first per setting, with
booleans OR-ed up the chain (they have no "unset", and these settings only ever tighten) and the
lock travelling *down* from any ancestor, so it cannot be escaped by uploading one level deeper.
A locked expiry longer than the plan allows is clamped, not rejected — the plan cap applies
first, same rule as `#161`. Admins set defaults; only the owner sets the lock.

`#234` folder templates — a saved layout stored as a JSON snapshot (`StructureJson`), never as
rows pointing at live folders: a template is a value, so editing or deleting it must not reach
into folders already created from it. Apply is idempotent by name — an existing folder is
counted in `skipped`, not failed — so a half-finished apply can be re-run and one template can
serve a second client via the optional `rootName` that nests the structure one level down.
Because depth caps at two, a template with its own children is applied flat under that root and
the skipped count says so. Apply reuses `FolderWriter.CreateAsync` rather than inserting rows of
its own; a bulk path with its own copy of the depth and uniqueness checks will eventually skip
one. Routes `GET/POST /api/workspace/templates`, `POST /api/workspace/templates/{id}/apply`,
`DELETE /api/workspace/templates/{id}`.


Tier 6 batch 5 (billing series), 2026-08-21: `#278` team seats, `#141` Studio overage,
`#112` e-invoice, `#192` PO + net terms — one migration `AddBillingProfilesAndInvoices`
(3 new tables `billing_profiles` / `invoices` / `invoice_lines`, plus `user_usage.OverageBytes`
and `AlertedThresholdPercent`). Tests: `BillingLedgerTests` (18). Design:
[`docs/billing-ledger.md`](billing-ledger.md).

The model is a **draft invoice per period**: mid-cycle seat and overage charges append to an
open draft as they happen, rather than being reconstructed from history at billing time.
Issuing freezes it — number, due date, and a copy of the buyer's company details, because an
invoice has to keep saying what it said when it was issued. An empty draft is never issued.
`GetOrCreateDraftAsync` checks the change tracker before the database: a caller that charges and
issues in one unit of work would otherwise create a second draft and issue an invoice with none
of the lines just recorded (a real bug, caught by the tests).

`#278` team seats — `PlanCatalog.IncludedSeats` (Free 1 / Pro 3 / Studio 10) with per-seat
pricing beside the plan prices, deliberately not in `PlanTierLimits` (usage caps read on every
upload should not depend on numbers only accounting cares about). `SeatBilling` is pure so the
seat meter, the finance CSV and the invoice line cannot disagree. Growth is charged and prorated
to the end of the period, never rounded to zero; shrinking frees seats for next period but is
not credited, because there is no refund flow and faking one is worse. A pending invite counts
as a claimed seat, or a workspace could park invites on seats it never pays for. Adding a seat
does not interrupt with a checkout. `GET /api/workspace/seats` is readable by any seat holder;
the roster export is admin-only. UI `/account/workspace/seats`.

`#141` Studio overage — `QuotaCheckResult.Metered` replaces `Exceeded` where
`PlanCatalog.AllowsOverage`, so Studio stopped hard-blocking with zero call-site changes while
Free and Pro still stop at 100% (an unbounded bill is a worse surprise than a refused upload
when nobody agreed to pay for overflow). `RecordAsync` tallies `OverageBytes` and rewrites a
single running overage line — an afternoon of uploading produces one charge, not two hundred.
Thresholds at 80/100/120% raise one notification each, with `AlertedThresholdPercent` making it
once rather than on every upload past the line.

`#112` e-invoice — `billing_profiles` captures company, MST and address; a company name *and* a
valid tax code (`^\d{10}(-\d{3})?$`) turn receipts into VAT invoices. Numbers are
`QS-{year}-{000001}`, with the unique index as the real collision guard so a concurrent issue
fails loudly. VAT 10% computed at issue and stored. The PayOS webhook adds the plan line and
issues under the same idempotency check as the payment event. **Not shipped: a certified VN
e-invoice** — Decree 123/2020 requires a licensed provider (Viettel/MISA); what ships is a
print-ready page plus CSV export, and the page says so in both locales. UI `/account/invoices`.

`#192` PO + net terms — PO reference on the profile, copied onto every invoice at issue. Terms
(0/30/60 only) are a request until a platform admin approves them; `EffectiveNetTermsDays`
returns 0 otherwise, so an unapproved request bills on issue like a card, and changing the
request clears a prior approval. `InvoiceOverdueJob` (hourly) flips issued to overdue once,
notifies and emails; only terms invoices can be late. `POST /api/admin/billing/invoices/{id}/paid`
reconciles a bank transfer manually, idempotently.


Tier 6 batch 6 (trust series), 2026-08-21: `#190` tenant isolation, `#191` SIEM streaming,
`#184` self-serve DPA, `#147`/`#323` SOC 2 readiness — one migration
`AddSiemSinksAndDpaSignatures` (2 new tables `siem_sinks` / `dpa_signatures`). Tests:
`TenantIsolationTests` (7) + `SiemAndDpaTests` (16). Design:
[`docs/enterprise-trust.md`](enterprise-trust.md).

`#190` tenant isolation — the mechanism is an explicit `Where` per query, at the call site,
never an EF global query filter: a global filter is lost silently to `IgnoreQueryFilters`, raw
SQL, or a projection built before it applies, and the failure mode is a cross-tenant read with
nothing in the logs. `TenantIsolationTests` is therefore the actual control, probing folders,
workspace management, audit, report, deliveries, invoices and streaming config with a *real id
from the other tenant* (a random GUID would pass even with scoping removed). `TenantGuard`
records `CrossTenantAccessBlocked` when the id exists but belongs to someone else, while the
response stays identical to a plain 404 — otherwise the error code is an existence oracle — and
a miss on an id belonging to nobody is deliberately not recorded, because the signal is a burst
and noise drowns it.

`#191` SIEM streaming — no outbox: `security_events` is already durable and append-only, so the
sink stores a `(CreatedAt, Id)` cursor and advances it only on a successful POST. At-least-once
by design; a SIEM de-duplicates on `externalId`, whereas a dropped audit event is unrecoverable.
Three formats (json / cef / splunk_hec) over one field set, with CEF delimiter escaping tested
because an unescaped `=` silently swallows the next field. HTTPS only, HMAC-SHA256 over
`{timestamp}.{body}` with the timestamp inside the signed payload so a captured batch cannot be
replayed as fresh. IPs ship hashed, as stored. A sink parks after 10 consecutive failures and
un-parks on save; a new sink starts its cursor at *now* rather than back-filling history nobody
asked for. The secret is write-only. Routes `GET/PUT/DELETE /api/workspace/streaming`;
`SiemShippingJob` every 5 minutes; UI `/account/workspace/streaming`.

`#184` self-serve DPA — the signature row *is* the artefact: the page renders the stored record
beside the published terms, so evidence and document cannot disagree, and printing makes the
PDF. Versioned, with `NeedsResign` flagging a signature against older terms — still a real
agreement, just not agreement to what is published today. Signing the same version twice is
refused, because a second row makes "when did they agree" ambiguous. Terms are public (a DPA
behind a login is one their legal team cannot review); only the signing panel needs an account.
IP hashed, signing audited. Routes `GET /api/dpa`, `POST /api/dpa/sign`; UI `/dpa`.

`#147`/`#323` SOC 2 readiness — `/soc2` publishes the control map, opening with **not certified,
no audit, no report**, because a readiness page that reads like a certification is what loses
the vendor review it was written to win. Ten criteria mapped with a three-valued status
("partial" is the honest answer for a control with no independent evidence yet), a named gap
list (no pen test, no written policy set, no scheduled restore drill with evidence, no
vendor-risk process, no auditor engaged) and a four-phase roadmap. Both locales.


Tier 6 batch 7 (storage series), 2026-08-22: `#171` S3-compatible storage backend, `#271`
scheduled workspace backup — one migration `AddObjectStoreAndBackupPolicies` (new table
`workspace_backup_policies`). Tests: `StorageAndBackupTests` (13). Design:
[`docs/object-storage-and-backup.md`](object-storage-and-backup.md).

`#171` S3-compatible storage — `R2StorageProvider` became `S3CompatibleStorageProvider`, and
that is nearly the whole diff: it always spoke S3 through the AWS SDK (R2 *is* S3-compatible),
and the single R2-specific line derived the endpoint from a Cloudflare account id. Making the
endpoint explicit config unlocks MinIO, Garage, Ceph, Wasabi and AWS S3 with no code change —
self-hosters were stuck on local disk for want of one value. New `ObjectStore` config section
with `ObjectStoreResolver.Resolve` as the single place that decides section precedence; the
legacy `R2` section still binds, because an options rename that breaks existing deployments on
upgrade is not a feature. `ForcePathStyle` defaults true (R2/MinIO/Garage need it, AWS tolerates
it). **Not included: data residency `#119`** — routing per-delivery to a regional bucket needs
the region to travel through ~10 `IStorageProvider` signatures plus a `StorageRegion` column and
a migration path for existing objects; that is its own change, not a footnote to a config
generalization.

`#271` scheduled workspace backup — destination is the *customer's* S3-compatible bucket with
their credentials, because a backup inside the same blast radius as the thing it backs up is not
a backup. The manifest is metadata plus storage keys, deliberately **not** file bytes: shipping
terabytes weekly is a bandwidth bill nobody agreed to, and the keys are what a restore actually
needs. Versioned shape (`Version: 1`), uploaded with `ChecksumSHA256` so a restore can verify
without trusting our record. Hourly tick, per-policy daily/weekly interval; pruning to N retained
copies happens *only* after a successful upload, since pruning on a run that produced nothing is
how a retention policy deletes the last good backup. Failures are recorded and the sweep
continues; the destination client has a 30s timeout and one retry. Run-now shares the scheduled
method — a test on a different code path proves nothing about the 3am run — and returns 200 with
the failure in the body, because the request was fine and the destination was not. HTTPS-only,
admin-only, write-only secret, one policy per workspace. **Restore is not automated**: the
manifest is a documented versioned JSON format, and a half-working import wizard would be worse
than a format an operator can read.


Tier 6 batch 8 (branding series), 2026-08-22: `#285` branded recipient page, `#286` custom
typography — one migration `AddWorkspaceBrands` (new table `workspace_brands`). Tests:
`WorkspaceBrandTests` (16). Design: [`docs/workspace-branding.md`](workspace-branding.md).

`#285` branded recipient page — logo, accent pair, header and footer copy, and a white-label
switch that drops the QuickShare wordmark (the brand half of `#81`). Scope is deliberately the
pages a workspace's *clients* open, never the signed-in app: a member of two workspaces would
lose any stable sense of where they are. The accent pair is contrast-checked at 3:1 using the
real WCAG luminance formula, because a brand that makes its own page unreadable is a support
ticket from the client who could not find the download button. Uploads are sniffed by magic
bytes rather than the declared content type; **SVG is refused** (a document format that can
carry script is not worth an XSS surface on a client's page) and so is GIF. Replacing an asset
writes a new key, since the public `/brand/{workspaceId}/{kind}` route is immutably cached and
an in-place overwrite would serve last month's logo. That route is anonymous by necessity — the
audience has no account — and streams through the app rather than presigning, because a
presigned URL expires while the page is open and a public bucket is not a given for a
self-hoster. A disabled brand serves 404: switching it off has to take it off the page.
UI `/account/workspace/brand`; brand cached 30 minutes with explicit eviction on every write.

`#286` custom typography — self-hosted woff2 only (no third-party font CDN, so the client's
browser talks to nobody but us), 512 KB per file, mapped to heading and body roles. **Type sizes
stay fixed**: `DESIGN.md` owns the scale and a brand swaps families only, so the renderer emits
no `font-size` at all and the tests assert it — that is the kind of constraint that erodes one
convenient exception at a time. Family names are validated against a restricted character set at
the write boundary, which is what makes rendering the stylesheet as a `MarkupString` safe;
`font-display: swap` plus a fallback stack keeps the download button from hiding behind a font
request that may never finish. The licence note is the uploader's own declaration and is
labelled as unverifiable.

**Not shipped**: per-folder brand override (a second resolution layer on every recipient page
for a setting changed once a year), and custom-domain-carried branding — `#67` was still open at
the time, so `#81` white-label stayed open with it, since brand control without domain control
is not white-label. (Batch 19 closed that gap.)


Tier 6 batch 9 (residency), 2026-08-22: `#119` data residency — one migration
`AddStorageResidency` (`users.PreferredStorageRegion`, `deliveries.StorageRegion`). Tests:
`StorageResidencyTests` (19). Design:
[`docs/object-storage-and-backup.md`](object-storage-and-backup.md).

Deferred twice from the storage batches, and taken alone here because it is a routing change to
the path every upload and download goes through.

Routing is **derived from the object key** (`uploads/{region}/{user}/{delivery}/{name}`) rather
than passed through `IStorageProvider` on every call. The explicit alternative — a region
argument on ten signatures and ~25 call sites — is more visible, but it means every present and
future path that touches an object has to thread it correctly, and one that forgets reads the
wrong bucket and reports "not found" for a file that exists. `Delivery.StoragePath` already
travels everywhere, so deriving the bucket from the key means an object is always findable from
the one thing that is always present. `InitR2MultipartUploadAsync` is the sole method taking a
region, because it is the sole call that creates a key.

**Backward compatibility is what makes the scheme safe rather than merely convenient**: every
pre-residency key has a user GUID where the region segment now sits, and region codes are
`^[a-z0-9]{2,12}$` so a 32-char hex string can never match one. Old objects resolve to the
default bucket exactly as before, and a test asserts precisely that. Only `uploads/` is
partitioned — avatars and brand assets stay put rather than scattering a logo across
jurisdictions.

Regions are configured per deployment as bucket-only (the R2 shape, one endpoint) or with an
endpoint override (the AWS and multi-site MinIO shape, where a region genuinely is a different
host); a region without its own endpoint reuses the default client. Half-configured regions are
dropped rather than failing startup. Entitlement follows the plans: **Studio routes per
delivery**, **Pro pins one region** on the account and its per-request override is deliberately
ignored (honouring it would hand Pro the Studio feature), Free and anonymous get the default.
An unknown or removed code **degrades to the default bucket instead of erroring** — residency is
a deployment capability, not a user input to validate.

**Nothing moves.** Changing the setting applies to future uploads only, and replacing a file
keeps the delivery's original region: relocating bytes across jurisdictions is a copy, a
verification and a delete, not a side effect of a settings save. The recipient page shows a
"Stored in {region}" badge, and shows nothing when the region is no longer configured.


Tier 6 batch 10 (gating series), 2026-08-22: `#162` approval workflow, `#176` recurring delivery
— one migration `AddApprovalAndRecurringDelivery` (new table `recurring_deliveries`, approval
columns on `deliveries`, two policy flags on `workspaces`, `DefaultRequireApproval` on
`workspace_folders`). Tests: `ApprovalAndRecurringTests` (19). Design:
[`docs/delivery-gating.md`](delivery-gating.md).

`#162` approval workflow — **ordering is the feature**. A held share gets
`Status = PendingApproval`, its recipient list parked on `Delivery.PendingRecipients` rather than
mailed, and the *sender's own* link mail withheld too: a mail saying "ready to share" for a link
that refuses everyone is worse than no mail. Approving is the moment anybody outside first learns
the link exists, which is why the release mails live in `ApprovalNotifier` next to the code that
withheld them. Both recipient paths report **404, not a distinct state** — whether a sender's
organisation reviews its shares is a fact about that organisation, and a separate status code
would make the endpoint an oracle for internal process. The requirement comes from the workspace
flag OR any folder above the delivery (tighten-only, riding the existing `#245` chain) and is
deliberately independent of `DefaultsLocked`, since a gate the sender can switch off is not a
gate. Self-approval is off by default; denial requires a reason and goes to trash rather than
destruction; a second approve is a 409; the queue is a query rather than a parallel table, oldest
first. Personal deliveries are never gated — there is no reviewer they could be waiting on.

`#176` recurring delivery — each cycle copies the source to a **new object with a new share
code** instead of re-mailing the original link, because a link that never rotates is a link that
leaks and last week's recipients should not open this week's numbers. Catch-up is **skipped, not
batched**: `ComputeNextRun` always moves strictly forward from now, so a week-long outage sends
one report on recovery rather than seven, and the next run is advanced *before* the attempt so a
throwing schedule does not hammer storage hourly. Monthly is clamped to day 28. Skip and pause
are separate controls ("not this week" vs "not for now"), resume re-bases on now, a missing
source disables the schedule with a readable reason, and recipients are normalised so the mail
loop cannot double-send. Generated cycles are never themselves gated: the schedule was approved
when it was created.

**Defect fixed in passing**: `CloneDeliveryHandler` built its key without a region, so after
`#119` cloning a region-pinned delivery silently moved the copy across a jurisdiction boundary.
Both the clone path and the recurrence job now carry `StorageRegion` — found by writing the next
feature against the rule rather than in production.

**Not shipped**: approval SLA timers with escalation (needs an escalation chain the seat model
does not have, and a timer that escalates to nobody is worse than none), and admin UI toggles for
the two workspace approval flags — the columns and enforcement are real and tested, the switches
are not built.


Tier 6 batch 11 (conversation series), 2026-08-22: `#289` delivery comments, `#180` Slack/Teams
posting — one migration `AddCommentsAndChannelHooks` (new tables `delivery_comments` /
`workspace_channel_hooks`, `deliveries.CommentsEnabled`, `workspaces.CommentPolicy`). Tests:
`CommentsAndChannelTests` (18). Design:
[`docs/comments-and-channels.md`](comments-and-channels.md).

`#289` comments — three independent gates, **all of which only close**: the sender's per-delivery
switch (off by default, because a share is one-way unless they say otherwise), the workspace
`CommentPolicy` ceiling, and privacy mode, which wins outright — a share that keeps no download
record must not accumulate a public thread instead, so the column is forced false at commit
rather than resolved later. `MembersOnly` restricts posting, not reading, which is the
distinction the policy is actually asked for. Anonymous recipients supply a display name and are
rate-limited by hashed address; the thread marks which authors are real accounts, because a
supplied name is a claim rather than an identity. Deletes are soft **and blank the body** — the
row stays so replies keep their context, and keeping the text would mean the delete was not one.
An anonymous author cannot delete their own: "same hashed address" is not proof of authorship and
would let anyone behind the same NAT remove somebody else's words. Replies are one level deep.

`#180` Slack/Teams — one `{"text": …}` body serves both platforms; per-platform cards would be
two renderers to keep in step for a difference no channel reader notices. The webhook URL is
treated as the credential it is: stored, never returned, blank-on-save preserves it, and the
audit records the **host** rather than the URL. HTTPS only. Posts are best-effort with a
5-second timeout and can never fail a share or a comment — the same inline pattern the recipient
emails in that handler already use, chosen over new queue infrastructure for a call that
normally takes tens of milliseconds. A hook failing ten times running is parked and un-parks on
save.

**Two `#119` defects found and fixed here**, both invisible to a green suite: `Delivery.StorageRegion`
was never assigned at commit (routing worked, the recorded region was always null, so the badge
never showed and every copy path passed null), and `ObjectStoreResolver` dropped the region table
whenever store config fell back to the legacy `R2` section. The suite could not have caught
either, because the test storage stub returned a made-up object key instead of the real shape
and no test configured a region. Both fixed, plus the stub now builds real keys and the factory
configures two regions, so residency is exercised end to end.

**Not shipped**: comment attachments (a second file feature hiding in a comment box), @-mention
routing (needs a participant directory recipients are not in), download events posted to the
channel, and comments in the GDPR export.


Audit of tier-6 batches 1–11, 2026-08-22. No new features; findings and fixes. Tests:
`AuditRegressionTests` (22), suite fully green at 747 for the first time since the tier began.

**Fixed — security.** Delivery comments (`#289`) bypassed every gate the delivery itself
enforces: no `DeliveryAccessService` (so an IP- or country-blocked caller could read and post to
the discussion about a file they could not open) and no lifecycle check (expired, revoked and
awaiting-approval shares kept serving their threads). This violated an invariant already written
into `AGENTS.md` by the compliance batch. `CommentGate` now applies both first.

**Fixed — never wired.** `ChannelNotifier.NotifyDeliveryAsync` was never called by anything, so
`#180`'s headline behaviour — announcing a *new delivery* in Slack/Teams — did nothing while the
flag was settable and documented. Now called from commit, and from approval release for held
shares. `BillingAccountResolver` was dead code, DI-registered and documented as authoritative;
deleted. Three `RoutePaths` constants pointed at pages that were never built; removed.

**Fixed — test hygiene.** The two `RecipientContactTests` failures reported as "pre-existing" for
nine batches were order-dependent assertions on the shared default user; scoped to per-test
accounts. A permanently red run is worse than no test, because it trains everyone to skim past
red — which is how the `#119` defects survived.

**Found, then fixed — UI gap.** `#162` (approval queue), `#176` (schedule list), `#180` (channel
config) and `#289` (comment thread) shipped API-only. The endpoints were complete and tested; the
screens a user would touch did not exist. Those four issues were reopened, and the batch below
built them.

**Root cause across all of it**: unit tests prove a function computes the right answer and say
nothing about whether anything calls it. `AuditRegressionTests` asserts reachability through
observable side effects, and page render coverage now exists for the eleven pages the suite had
never loaded.

## Tier 6 — batch 11: the four missing screens (#162, #176, #180, #289)

The audit's own finding, closed out. Four pages, no new subsystems:

- `/account/workspace/approvals` — policy switches and the pending queue (`#162`)
- `/account/workspace/channel` — Slack/Teams webhook and its health (`#180`)
- `/account/recurring` — schedule list and create form (`#176`)
- the comment thread on `Download.razor`, plus the `upload-comments` toggle that makes
  `CommentsEnabled` settable by a sender for the first time (`#289`)

Two API gaps had to close before the screens could work. `PATCH /api/workspace` gained
`RequireDeliveryApproval`, `AllowSelfApproval` and `CommentPolicy` as *nullable* fields — null
means leave alone, so a screen can change one policy without resending the workspace name and
seat count it does not show. And `CommitR2Request` now carries `CommentsEnabled`.

The comment thread is server-rendered rather than HTMX-fetched. That is a deliberate exception to
the lazy-load default: `DownloadNoScriptTests` requires the recipient page to work without
scripting, and a thread that arrives over `hx-get` would be a blank section for a reader who has
JS off. Only the posting form needs JS, and `<noscript>` hides it.

**Not shipped**: real-time updates (the thread is as fresh as the page load — polling a recipient
page nobody is watching is cost with no reader), comment editing from the UI (the endpoint
exists; a delete-and-repost covers it), and approval bulk actions (a queue that needs bulk
approve is a queue whose policy is wrong).

**Found while building them — ten dead page scripts.** `wwwroot/js/**` is `tsc` output and is
gitignored; `CLAUDE.md` said to write page JS there. Six earlier tier-6 screens (workspace, brand,
billing, backup, SIEM, DPA) had their JS written straight into that folder, so it was never
committed: on a fresh clone every button on those pages did nothing. All ten scripts — the six
plus this batch's four — are now TypeScript in `ts/`, and both instruction files were corrected.
Collapsing their duplicated `send`/`reload`/`value` helpers into `ts/page-actions.ts`
(`window.qsPage`, loaded globally) made the ten of them shorter than the six had been.

**What the audit's lesson looks like in tests here**: `B2BUiTests` asserts the *page* carries the
data, not that a handler returns it. A 200 would not have caught the defect being fixed — every
one of these endpoints already returned 200 to nobody.

## Tier 6 — batch 12: enterprise identity (#128, #146, #181, #219)

The identity cluster, built as one subsystem because it answers one question in four places:
*who is this, and does the organisation still say they belong here?*

- `#181` OIDC SSO — Google Workspace via the `hd` claim, Entra ID via `tid`, JIT seat provisioning
- `#146` SCIM 2.0 — `/scim/v2/Users`, bearer token per workspace, deprovision revokes the seat
- `#128` TOTP — RFC 6238, no dependency, recovery codes, single-use codes
- `#219` MFA policy — workspace-wide requirement, grace window, SSO exemption, posture number

**`#128` was not on the tier-6 list, and was built anyway.** `#219` asks to enforce "TOTP or SSO",
and there was no TOTP in the codebase — the issue's own evidence said `#128` was per-user opt-in,
which was not true either. Shipping the enforcement alone would have been a policy with nothing
to enforce: exactly the defect the previous batch's audit spent its time correcting. So `#128`
was built as its prerequisite and closed with it.

**`#139` SAML was deliberately left.** It needs a signing library, certificate handling and IdP
metadata parsing. Folding it in would have meant doing it thinly, and a thin SAML implementation
is a security surface rather than a feature.

**One design hole found by a failing test.** Two test workspaces both bound `acme.com`, and the
seat landed in whichever registered first. That is not a test bug — in production it means any
workspace could bind itself to a directory it does not own and absorb everyone who signs in from
it. A directory is now claimable by exactly one workspace (`sso_directory_taken`).

**Two bugs found by the FK.** `SecurityEvent.UserId` is a foreign key, and both the SSO and SCIM
audit paths were writing `Guid.Empty` for a seat with no account behind it yet, plus an unset
`Id`. SCIM now attributes a pending-seat event to the workspace owner with the address in
metadata, so the workspace audit lens stays complete instead of silently dropping those rows.

**Not shipped**: SAML (`#139`), SCIM `/Groups`, session revocation on deprovision (needs the
session store from `#105` — the seat dies at once, but a personal-account cookie outlives it),
and per-user MFA exemptions.

Design and invariants: `docs/enterprise-identity.md`.

## Tier 6 — batch 13: SAML 2.0 SSO (#139)

The batch deferred last time, done on its own because it deserved to be.

SP metadata, SP- and IdP-initiated sign-in, signature/issuer/audience/window validation, durable
replay protection, and the same JIT seat provisioning the OIDC path uses.

**The library does the crypto.** XML digital signatures have a long history of implementations
that validate *a* signature rather than the signature over the element they then read, and every
one of those bugs is an authentication bypass that fails silently. ITfoxtec handles signature,
canonicalisation and parsing; what is ours is which workspace an assertion belongs to, whether its
issuer is accepted, and whether it has been seen before.

**The dependency brought a real finding.** ITfoxtec pulls `System.Security.Cryptography.Xml`
10.0.7 transitively — five high-severity advisories, in XML signature handling, the exact code
this feature trusts. The NuGet audit failed the build, correctly. It is suppressed rather than
pinned because the assembly ships in the shared framework and NuGet prunes the package (NU1510
refuses an explicit reference), so the real mitigation is a patched runtime. That is now written
down as a deployment obligation rather than left implicit.

**Replay protection is a table, not a cache.** `CacheService` no-ops when caching is disabled,
which would have let a config flag silently switch off a security control, and a per-process cache
would not see a replay aimed at a second instance. A unique index on the assertion id makes
consuming an assertion an insert and a replay an insert failure — atomic across instances.

**Not shipped**: single logout, signed AuthnRequests, encrypted assertions, IdP metadata import,
multiple bindings per workspace.

Design and invariants: `docs/saml-sso.md`.

## Tier 4 — batch 14: multi-file deliveries and folder upload (#79, #43)

WeTransfer parity, and the largest shape change to `Delivery` since it was written.

Additive on purpose: `Delivery` keeps describing file #1 exactly as before, two new columns
describe the bundle, and a new `delivery_files` table holds the contents. Every path written
before multi-file — previews, virus scan, expiry, backup, SIEM, the download page's single-file
phase — keeps working untouched and keeps being correct for the single-file case that is still
almost every delivery.

The invariant that makes it work: **every delivery has at least one file row, including the ones
that predate the feature.** The migration backfills them. Without that, every consumer needs a
fallback and the ones that forget show an empty list for most deliveries in the system.

**The security property**: a committed object key must belong to the delivery claiming it. Commit
is client-supplied, so without `R2ObjectKey.BelongsTo` a caller could name somebody else's object
and have it served under a share code they control. There is a test that tries exactly that with
a real victim key.

**Two things learned the hard way**, both now in `docs/multi-file-deliveries.md`: writing to
`Response.Body` directly under FastEndpoints silently discards the bytes (the caller gets an empty
200 that presents as a corrupt archive), and duplicate zip entry names make most extractors drop
all but the first file.

**Also this batch — six tier-4 issues closed on verification, not rebuilt.** #16 file requests,
#233 extension requests, #103 web push, #246 notification center, #57 upload-from-URL and #94
Office preview were all already shipped with UI and tests. Each was checked the way the audit
taught — that a person can reach it, not just that the code exists — before closing.

**Not shipped**: per-file expiry, adding files after commit (that is versioning, #110), reordering
(#275), Zip64, bundle-aware previews.

## Tier 5 — batch 15: the developer platform (#115, #39, #172, #223, #288)

API keys and outbound webhooks, built as one subsystem because a webhook nobody can authenticate
against and a key with nothing to call are each half a feature.

- `#115` API keys — `qs_` prefix, SHA-256 stored, returned once, coarse scopes, optional expiry
- `#39` outbound webhooks — three events, HTTPS only, queued not inline
- `#172` HMAC-SHA256 over `{timestamp}.{body}`, exponential backoff, endpoint parking
- `#223` event log with the response we got back, plus a test console on the real path
- `#288` replay — original payload, new row

**The signing decision worth reviewing**: the timestamp is inside the signed material. Signing
only the body means a captured request stays valid forever and the receiver cannot tell a replay
from the original. Same construction as the SIEM sink, so one shape to document.

**Queued, not inline** — deliberately unlike the chat notifier (#180), which posts inline because
a chat post is best-effort and forgettable. A webhook is an integration contract and has to
survive the receiver being down.

**Privacy mode suppresses `delivery.downloaded`**, alongside the analytics event and the download
record. A share that keeps no record must not export one instead.

**Also this batch — twelve tier-5 issues closed on verification**: #23 vanity links, #61 admin
moderation, #126 audit trail, #187 privacy mode, #189 SSRF hardening, #217 recovery codes, #235
trusted devices, #239 feedback, #291 one-time codes, #294 public stats, #324 email gate, #51
branded download page. All already shipped with UI and tests; each checked for reachability first.

**Not shipped**: per-scope enforcement on existing endpoints (keys identify their owner; the
endpoints still authenticate by cookie), secret rotation with an overlap window, per-key usage
analytics (#273), API versioning (#199), more event types.

Design and invariants: `docs/developer-platform.md`.

## Tier 5 — batch 16: subscription lifecycle (#98, #114, #229, #174)

Everything that happens to a subscription between signing up and leaving. One batch because they
interlock: the save offer in the cancel flow *is* a pause or a promo code.

- `#98` promo codes — percent or fixed, limits, expiry, plan restriction, first-time-only
- `#114` proration — the unused part of the current period credits an upgrade
- `#229` vacation hold — banks the remaining paid days, drops to Free, hands them back
- `#174` cancel flow — closed-set reason plus free text, with an offer matched to the reason

Because it is money, the tests are written as things that must never happen: a negative price, a
second redemption by the same account, a quote consuming a limited code, a pause granting free
time or burning paid time, a proration credit paying out.

**Quote and redeem are separate calls.** Redeeming at checkout would burn a limited code on every
abandoned cart — a hundred-use code would run out on twenty sales.

**The save offer matches the reason.** Somebody taking a break wants a hold, not a discount; and
the retention discount is a promo code an operator creates (`STAY`), not a percentage hard-coded
into the cancel flow, because that would be a price change nobody signed off on.

**A bug the UI test caught:** pausing drops the account to Free, and the billing page was then
hiding the subscription entirely — including the resume button, from the only person who needs it.

**Also — #73 annual billing verified and closed**: `BillingPeriod.Yearly`, yearly pricing keys and
the monthly/yearly toggle on the upgrade page were already shipped and reachable.

**Not shipped**: admin UI for creating promo codes (they are rows an operator creates), automatic
resume at a chosen date (`MaxPauseDays` exists, nothing enforces it yet — stated rather than
half-built), code stacking, refunds, dunning (#140), multi-currency (#120).

Design and invariants: `docs/subscription-lifecycle.md`.

## Tier 5 — batch 17: sessions you can actually revoke (#105, #173)

The auth cookie was self-contained: it proved who somebody was and expired on its own schedule,
and nothing on the server could take it back. A "sign out everywhere" button over that would have
been decoration, so this adds the session store underneath it.

The cookie now carries a session id and a row decides whether it counts. **The validation result
is deliberately not cached** — caching it is exactly what would make revocation take effect
eventually rather than on the next request. One indexed read per authenticated request is the
honest price of a revoke button that works.

Two clocks, because they answer different questions: the cookie's 30 days ask how long somebody
may stay signed in, the 14-day idle timeout asks how long after they walked away.

**Legacy cookies are allowed through** — a principal with no session claim validates. Rejecting
them would sign out every logged-in user on deploy to enforce a rule none of them had a chance to
satisfy. The hole closes itself within 30 days.

**Closes a gap this backlog already recorded.** When SCIM (#146) shipped, the docs said plainly
that deprovisioning killed the seat but not the browser session. It does now, with a test that
drives the real SCIM PATCH.

**Not shipped**: per-session IP display (the hash is stored for privacy; showing a hash helps
nobody), device naming (UserDevice already models the device), step-up re-authentication,
per-workspace idle timeouts.

Design and invariants: `docs/sessions.md`.

## Tier 5 — batch 18: passkeys and magic links (#68, #132)

Two more doors into an account that is still created through an identity provider. That single
fact shapes the design: losing every passkey is not a lockout, and a magic link cannot create an
account.

The cryptography is Fido2NetLib's. What is ours is challenge storage (a table, not the cache —
same reasoning as the SAML assertion store), usernameless sign-in, and what a failure is allowed
to say: one error for every passkey failure, because distinguishing "no such credential" from
"bad signature" tells an anonymous caller which credential ids exist.

**A test caught a real leak.** The magic-link endpoint is supposed to answer identically whether
or not an address has an account. With email unconfigured, a known address threw a 500 and an
unknown one returned 204 — exactly the account-existence oracle the design set out to prevent.
The send is now wrapped: a failure to send is an operational problem and belongs in the log, not
in a response a stranger can diff against another one.

**#118 is deliberately left open.** It asks for a passkey recovery escape hatch, and that premise
does not hold here — every account has a working identity provider behind it, and accounts with an
authenticator already have recovery codes from #128. Writing that on the issue is more useful than
closing it with a hand-wave or building a redundant mechanism.

**Not shipped**: passkey as a step-up factor, conditional-UI autofill passkeys, attestation
verification (we do not care which authenticator model was used), magic links for new addresses.

Design and invariants: `docs/passwordless-auth.md`.

## Tier 6 — batch 19: white-label and the preview watermark (#81, #155)

The agency batch. `#285`/`#286` gave a workspace its own look; this gives it its own address,
and gives the previews it shares a name across them.

`#81` is mostly composition — sub-accounts are seats (`#106`/`#278`), per-tenant keys are the
developer platform (`#115`), billing is PayOS. The part that did not exist was the hostname.

**A claim is not a grant.** `workspace_domains` stores what an admin typed and changes nothing
until DNS agrees: every read goes through `VerifiedAt`, never `Host`. Two unique indexes carry
it — one per workspace, and one on the host itself, which is the index that closes the hole
batch 12 found in SSO directories, where two workspaces claiming one directory meant whoever
registered first absorbed the other's people.

**Verification is a DNS resolution, not a TXT record.** The domain only works if the agency
points it at us, so the check asks exactly that and asks for nothing else. `Dns` from the BCL
does it — no DNS client dependency. It is re-runnable and it can take a domain away: a host that
stops pointing at us stops being verified, because staying verified would keep us minting links
to an address that no longer reaches the files.

**One place builds a recipient link.** `WorkspaceDomainReader.DeliveryUrlAsync` now serves the
commit response, the recipient mail, the sender's own copy, the channel post, the approval
release, the recurring cycle and the expiry reminder. A link branded in the confirmation mail
and unbranded in the reminder is worse than no white-label at all — and the seven call sites had
each been formatting `BaseUrl` themselves.

**A verified host serves exactly one workspace.** `Download.razor` refuses a share from any
other with a plain not-found: an agency's client seeing somebody else's file under the agency's
own hostname is the worst failure this feature has, and a redirect would confirm the code
exists.

`#155` is a tiled SVG behind a CSS `::after` — no canvas, no re-encode, the stored object
untouched and the download byte-identical. The text is the workspace's brand line or the
sender's name, escaped twice (XML, then percent-encoding) into the data URI. On a free plan the
commit is **refused** rather than silently storing `false`: a sender told their preview carries
their name when it does not has been told something untrue about who can screenshot their work.
The overlay is defeatable by anyone determined — every overlay is; what it removes is the casual
screenshot-and-forward, and burning pixels into a transcoded stream is the upgrade path.

Tests: `WhiteLabelTests` (16) plus two page-render cases in `B2BUiTests`; suite green at 922.
Design: [`docs/white-label.md`](white-label.md).

**Not shipped**: multiple domains per workspace, apex domains (needs ALIAS/ANAME at the edge),
certificate issuance (the edge terminates TLS for the custom host — an operations step, written
down rather than performed by the app), per-tenant sender addresses in outbound mail (DKIM
delegation is a second verification subsystem), and custom watermark text.

## Tier 6 — batch 20: the WebDAV mount (#195)

A workspace as a network drive, for the teams the JSON API was never going to reach: the nightly
job that writes to a share, the designer who drags files into a folder window, the shop
migrating off FTP.

**One filesystem, two protocols eventually.** `WorkspaceFileSystem` owns what a path *means* —
root folders, shared folders (#151) one or two deep, deliveries as files — and WebDAV is only
the first consumer. `#265`'s S3 gateway is the second, and the reason the mapping is a class
rather than a handler: two protocols that disagreed about which files exist would be two
products.

**The namespace is the workspace, never a personal pile**, and a share still awaiting approval
(#162) is not listed — a drive is not a way around the gate.

**A mount read is owner-side.** It does not count a download, run the recipient gates or touch
the stats: this is the team looking at its own file, and charging it to the recipient statistics
would corrupt the only number the sender asked for.

**Writes obey the form's rules because the rules do not live in the form**: plan size cap
(checked on what landed, since a chunked PUT declares no length), monthly quota, storage region
(#119), locked folder defaults (#245). A folder whose locked policy demands a password refuses a
mount write outright — the protocol has no field for one, and inventing a password leaves nobody
to tell it to. PUT over an existing name *replaces* that delivery and keeps its link, which is
both what a filesystem client means by PUT and what `#72` already calls replace-keep-link.

**Credentials are the existing API key over Basic** — no second secret, no second place to
revoke — and the scope split is deliberate: `read` for PROPFIND/GET, `write` for PUT/MKCOL,
**`manage` for DELETE**, because a key that drops a nightly report should not be able to empty
the drive. There is no anonymous mount.

**Class 1 only, and the table says so.** No LOCK, so rclone/davfs2/Cyberduck read and write
while Explorer and Finder mount read-only. A fake LOCK returning a token while locking nothing
is worse than none: it tells a client its exclusive access is real. `Depth: infinity` is refused
rather than walked.

**A stub that was lying, found by the first test to need it.** `LocalDiskStorageProviderStub`
discarded uploaded bytes, so no stream-in/stream-out path could be tested while the suite looked
green — the same defect class batch 11's audit found in the object key. It now holds what it is
given, and the round trip is asserted.

Tests: `WebDavTests` (18); suite green at 940. Design:
[`docs/protocol-mounts.md`](protocol-mounts.md).

**Not shipped**: LOCK/UNLOCK (and therefore read-write Explorer/Finder — #308 is what a real
LOCK should map onto), MOVE/COPY, PROPPATCH, and the S3 gateway itself.

## Tier 6 — batch 21: the S3-compatible gateway (#265)

The second dialect over the filesystem batch 20 built. Every backup tool, CI job and sync client
already speaks S3, so the protocol is the integration: a procurement pipeline points rclone at a
workspace and writes no code at all.

**The bucket is the workspace, the key is the path**, so the gateway and the WebDAV mount are two
accents rather than two products. Where S3 differs from a filesystem, S3 wins: a nested key
creates the folders it needs (there is no MKCOL to call), deleting something absent succeeds, and
a short listing carries a continuation token instead of stopping silently.

**One credential in this product is stored reversibly, and the protocol is why.** SigV4 is a
shared-secret HMAC — verifying means recomputing from the bytes the client holds, which a hash
cannot do. So the gateway has its own credential row rather than reusing an API key: reusing
would have meant storing API keys reversibly too, trading the hashing invariant that protects
every other caller for one protocol's convenience. The secret is sealed with the DataProtection
keyring the email tokens already use, and losing that keyring means reissuing — written down
rather than worked around with a second secret store.

**A signature covers a hash of the body, not the body.** Verifying only the signature would leave
every upload payload unauthenticated while the request looked signed. The body is therefore
checked as it streams: hashed against the declared digest, or — for the `aws-chunked` framing the
AWS CLI sends — verified chunk by chunk along the signature chain from the seed. Verifying frames
without the chain would let anything be spliced mid-body. A 15-minute skew window closes the
replay the signature itself says nothing about.

**Multipart answers `NotImplemented`.** Our storage provider's multipart is presigned
client-to-bucket, the wrong shape for parts that arrive here, and a gateway that accepted an
upload id and then lost parts is worse than one that says no. rclone's default cutoff (200 MiB)
clears it; the AWS CLI's (8 MB) is one config line, and both are on the setup screen.

**The suite signs its own requests.** A stub that "authenticates" would prove nothing about
whether a real client can talk to us or whether a forged one cannot — so `S3GatewayTests` carries
the client half of SigV4, including a hand-built chunked body and a deliberately broken chain.

Tests: `S3GatewayTests` (20); suite green at 960. Design:
[`docs/protocol-mounts.md`](protocol-mounts.md).

**Not shipped**: multipart upload, presigned URLs (query-string auth), `POST ?delete` batch
deletes, bucket policies and versioning.

## Tier 6 — batch 22: voice commands (#277)

Hands-free navigation, built as an alternative *input* to the command palette (#153) rather than
as a second menu.

**No registry of its own.** `ts/voice.ts` matches what it heard against the palette's own
entries — the same labels, the same `data-keywords` the typed filter uses — so a command that
works typed works spoken, in whichever language the palette is rendered in. A parallel list of
voice commands would drift from the real one the day somebody adds a page.

**Nothing runs before it is shown.** Recognition mishears; a mishearing that navigates silently
is the failure mode worth designing against. So the status line says what was heard and what is
about to happen, waits, and goes — Escape or the word "cancel" stops it. The undo for an action
that has not happened yet is simply not doing it. The line is an `aria-live` region, because the
user this is for may not be watching the screen.

**The mic hides itself where the browser cannot listen**, rather than offering a dead button,
and the fallback is named in the message: the palette does the same things with Ctrl+K.

**Honest about privacy.** The issue claimed audio never leaves the device. That is not true of
Chrome, whose Web Speech implementation sends audio to a vendor service; only the *matching* is
local. The UI does not repeat the claim, and `docs/voice-commands.md` says which part is which.

**A CSS bug found by the test.** `hidden` on the trigger did nothing: the button class sets its
own `display`, which beats the attribute, so the "hidden" mic was still on screen and still
dead. Any `[hidden]` on a `db-btn` anywhere has the same problem.

Tests: `VoiceCommandBrowserTests` (5) — a fake `SpeechRecognition` is injected and spoken into,
because the part worth testing is what happens *after* recognition. Browser suite green at 36,
server suite at 960.

**Not shipped**: a wake phrase (always-on listening is a microphone nobody asked to leave open),
dictation into fields, and voice actions beyond navigation — "delete that file" needs a target
the palette does not model.

## Tier 6 — batch 23: crypto payments (#225)

A second payment rail behind the same intent, for the segment without a card: crypto-native devs
and international freelancers.

**One money path, two rails.** Plan, price, proration and promo quoting are computed once and the
provider is a column on `PaymentIntent`; only where the buyer is sent and what tells us they paid
differ. The alternative — a provider with its own pricing — is two products, and the day they
disagree a customer is charged something nobody can explain.

**Onramp, never custody.** The provider settles to fiat, so refunds and VAT stay on the existing
invoice path and nobody here holds a wallet, a key or a volatility problem.

**The webhook is the activation, and its signature is all of the security.** A chain confirmation
outlasts any redirect, so no return URL proves payment on this rail — the HMAC over the raw body
is the only thing between a stranger and a free Studio plan. Verified *before* parsing, because
parsing and re-serialising changes the bytes that were signed: the exact mistake that once made
every real PayOS webhook fail to verify here.

**Everything unconfirmed is acknowledged and ignored** — 200 for pending, delayed, failed and for
charges we cannot match, because a provider retries anything that is not a 200 and none of those
are failures it can fix by resending.

With no API key configured the button is not rendered: a payment method that cannot charge is
worse than one that is missing.

Tests: `CryptoPaymentTests` (8) — the charge is stubbed, the signature check deliberately is not.
Suite green at 968. Design: [`docs/payment-architecture.md`](payment-architecture.md).

**Not shipped**: self-custody, crypto refunds (fiat, through the provider, as a support
conversation), recurring crypto renewals (a hosted charge is one-shot — renewal falls back to the
card rail), and the pricing-page badge.

## Tier 6 — batch 24: OCR search (#243)

Find a file by the words inside the picture — a screenshot of an invoice, a photo of a
whiteboard, a scanned receipt.

**Opt-in per file, images only, never in privacy mode.** Reading somebody's screenshot is a data
touch and not one to make by default. The privacy gate is applied at commit, on the flag itself,
so the column is already the truth and the next consumer of it inherits the rule rather than
having to remember it.

**The engine is the installed binary, not an interop package.** `tesseract` runs as a
subprocess: one apt line in the Dockerfile, nothing in the build, and a host without it reports
unavailable and never offers the feature. Local rather than a cloud vision API because the
privacy page says files are not shipped to third parties, and an OCR feature that did would
quietly make that untrue.

**A null result is still a result.** The job writes a row either way — without it the same
unreadable screenshot would be re-processed on every tick, forever.

**Text lives in its own table.** The delivery row is read on every list, download and recipient
page; a kilobyte of scanned invoice riding along on all of them would be paid for constantly to
serve the one query that wants it.

**One search box, and the result says why it matched.** Name or image text in a single predicate
— a separate "search inside images" toggle would make people choose which search to run before
they know which one will find it — and an OCR hit carries an `in image` badge, because otherwise
it looks like a name match that is not there and the search looks broken.

Tests: `OcrSearchTests` (7) — recognition is stubbed, because a suite that shelled out to
tesseract would be testing tesseract. Suite green at 975. Design:
[`docs/ocr-search.md`](ocr-search.md).

**Not shipped**: PDF text extraction (a PDF has its own text layer — a different path), re-index
on demand for files uploaded before the sender changed their mind, match highlighting in the
preview, and language detection beyond the configured `eng+vie`.

## Tier 6 — batch 25: audit of batches 19–24

No new features. Six batches shipped since the last audit (white-label, WebDAV, the S3 gateway,
voice, crypto, OCR), and the same failure shape turned up twice: **a rule enforced at the call
site that needed it instead of at the thing every call site goes through.**

**Fixed — the tenant host was scoped on one page.** `#81` checked that a verified custom domain
only serves its own workspace's shares — on `Download.razor`. `/d/{code}/stats` (#294) sat one
route away with no check at all, so an agency's domain would happily render another workspace's
download statistics. The check now lives on `WorkspaceDomainReader.IsForeignHostAsync` and both
pages call it.

**Fixed — the whole app answered on a tenant domain.** Sign-in, the account area, the admin
surface and the marketing pages were all served under an agency's hostname: unbranded on
purpose, and an invitation to type our credentials into a domain we do not control.
`CustomDomainMiddleware` now serves recipient surfaces (and the API and assets they are built
out of) on a tenant host and redirects everything else to the canonical address — a redirect
rather than a 404, because the visitor still wanted the product.

**Fixed — suspension did not reach the new surfaces.** An abuse-suspended delivery (#53) stayed
listable and downloadable through the WebDAV mount and the S3 gateway, and was still queued for
OCR indexing. "It is the owner's own file" is the argument that fails here: suspension is
applied *to* the owner. Both mount paths and the OCR job now exclude it.

**Checked and found sound**: the mount's approval gate (#162 shares never appear), the S3
signature and chunk-chain verification, the crypto webhook's idempotency, the OCR privacy-mode
gate, and the voice layer's registry reuse.

**Accepted as-is**: the S3 gateway creates folders for a nested key before the upload can fail
on quota, which can leave an empty folder behind — visible, harmless and deletable, where the
alternative is a transaction spanning a network upload.

Tests: `TierSixAuditTests` (5), each one verified to fail against the code before its fix.
Server suite green at 980, browser suite at 36.

## Tier 6 — batch 26: API keys that authenticate something (#115 follow-up)

Not a new feature — the closing of a hole the mounts exposed. `#115` shipped API keys with
scopes, a management screen and revocation, and **nothing accepted them**: every endpoint read
the auth cookie, so a `write` key could not create an upload and a `read` key could not read a
delivery. The permissions on the developer screen described something nothing consulted. The
first consumer of a key in the whole product was the WebDAV mount, four batches later.

`ApiKeyAuthenticationMiddleware` makes a key authenticate the JSON API as its owner. Middleware
rather than an authentication scheme, so every endpoint keeps working untouched — the principal
carries the same provider claims a cookie does.

**Scope by method** (read for GET, manage for DELETE, write for the rest): coarse on purpose,
matching how the scopes are described. A per-endpoint permission table looks thorough and drifts
the first time somebody adds a route without remembering it exists.

**A key is second-factor-verified by construction.** It is an independent credential, unphishable
out of a session and revocable in one click, and a machine caller cannot answer a TOTP prompt.
Without that claim, enrolling an authenticator would silently kill every integration on the
account — the kind of failure nobody connects to the setting that caused it.

**Placement is load-bearing**: between `UseAuthentication` and `UseAuthorization`. Authorization
evaluates the endpoint's requirements on the very next line, so a principal built any later
arrives to a 401 that has already been decided — which is exactly what the first run of the new
tests showed.

Tests: `ApiKeyAuthTests` (8). Suite green at 988. Design:
[`docs/developer-platform.md`](developer-platform.md).

**Not shipped**: per-endpoint scopes, key-scoped rate limits distinct from the session ones, and
IP allowlisting per key.

## Tier 6 — batch 27: the Outlook add-in (#194)

One button in the compose window: the 25 MB attachment becomes a QuickShare link, with the
account's usual expiry and limits, and the attachment comes off the message.

**Batch 26 was its prerequisite.** The pane runs in a third-party webview where our cookie does
not reliably travel, so it authenticates with an API key — and until last batch no endpoint
accepted one. Building the pane first would have meant inventing an add-in-specific credential,
which is how a product ends up with three of them.

**The manifest is served, not shipped.** Every URL in it is absolute and must match the
deployment; one carrying another hostname installs cleanly and then silently does nothing. The
add-in id is fixed, because Outlook keys the install on it — a new id is a duplicate, not an
update.

**Attachments are removed only after their link exists**, or a failure would lose the file from
a mail nobody has sent yet. The upload is the same three steps the web upload takes, so there is
no add-in-specific ingest path to keep in step.

**One CSP exception, one path wide.** Office serves its script from its own CDN and frames the
pane, so `/addins` answers with a policy that allows both. Everywhere else keeps
`frame-ancestors 'none'`, which is what stops the rest of the app being clickjacked.

**Unverified against a real client, and the docs say so.** What is tested is what we serve: the
manifest's shape and URLs, the pane rendering outside the app shell, and the CSP exception being
scoped. Whether Outlook behaves as intended is a claim only a real Outlook can settle, and there
is no Office host in CI.

Tests: `OutlookAddinTests` (5). Suite green at 993. Design:
[`docs/outlook-addin.md`](outlook-addin.md).

**Not shipped**: the AppSource listing and Entra registration guide (a submission process, not
code), per-attachment selection, and any settings inside the pane.

## Tier 4 — batch 28: filing deliveries after upload (#244, #284)

Tier 6 is done bar two deliberate deferrals, so this picks tier 4 back up. Folders (#151) could
only ever be chosen *at* upload; nothing could refile a delivery afterwards, which made a folder
tree something you had to get right in advance.

**Move is a bulk action, because that is what it is.** `#244` (move between folders) and `#284`
(move many at once) are one verb with different selection sizes, so both are the existing bulk
endpoint's third action rather than a new route — the deliveries page already has multi-select
for extend and trash, and it now has a destination picker next to them.

**A move never turns a personal share into a team one.** A delivery outside the workspace is
skipped, not filed: that would be a *share*, handing colleagues a file its owner never offered
them. A folder belonging to another workspace is refused outright rather than reported as
skipped — the caller named a destination that is not theirs, and silence would look like the
move worked.

**Folder policy applies on arrival.** Locked defaults (#245) only ever tighten: privacy switches
on, an expiry window shortens, and a folder whose policy demands a password refuses the move
entirely, because this path has nowhere to ask for one. Otherwise a file could escape a policy
simply by being uploaded elsewhere and dragged in.

**Copy is the clone (#218) with a destination.** A query parameter, not a body: the endpoint has
always been called with no body at all, and requiring one to add an optional field breaks every
existing caller — which the suite caught the moment it was tried.

**`#221` was already done.** Per-folder storage breakdown shipped with the folder batch; the
workspace folders page has shown count and bytes per folder since. Closed rather than rebuilt.

Tests: `FolderMoveTests` (8). Suite green at 1001.

**Not shipped**: drag-and-drop onto a folder (`#275` wants manual ordering too — both need a
sort column and a drag layer, which is its own batch), and moving between workspaces.

## Tier 4 — batch 29: who took it, and which parts (#37, #154)

Both issues shipped half of themselves batches ago and nobody noticed the other half was
missing.

`#37` delivered N recipients, per-plan caps and one mail each. Its acceptance also says *per-
recipient status* — who opened, who downloaded — and a sender who mails five people and reads
“3 downloads” still does not know which two to chase. `#154` asked for a page listing several
separate files with per-item downloads; `#79`/`#43` gave the page and the per-file links, and
left the counting.

**Attribution is a token per recipient, in their own mail.** It is a tracking identifier and it
behaves like one — a forwarded link attributes the person it was sent to, because the token
travels with the URL. It marks nothing and gates nothing: it cannot open a share that is closed,
and stripping it costs a recipient only their row.

**Rows are written whether or not mail can go out.** The list is a record of who this was
addressed to; a deployment with no mailer configured should still be able to tell its sender
that. The first run of the tests found the opposite — no mailer, no rows.

**Privacy mode records nothing at all.** Not the row, not the token, not the open. A share that
deliberately keeps no download record must not keep a per-person one instead — and the sender is
*told* that is why the panel is empty, rather than shown a blank.

**A token is scoped by delivery as well as by token.** One share's token must never resolve
against another's row, which is the same rule the per-file download already follows for file ids.

**Per-file counts are their own number.** Taking two files out of a bundle of five is one
download of the share and one of each file; a sender who cannot tell those apart cannot tell
which file nobody wanted. Only shown for bundles — for a single-file delivery it is the same
number said twice.

The sender sees both in a per-row panel that loads when opened: most rows are never expanded,
and a list that costs two queries per delivery would be paid for on every page of twenty.

Tests: `RecipientActivityTests` (8). Suite green at 1009.

**Not shipped**: per-recipient revocation (one recipient losing access while the others keep it
needs per-recipient links, not per-recipient markers), re-send to one recipient, and read
receipts on the recipient page — nobody asked to be told they were watched.

## Tier 4 — batch 30: the accessibility pass (#95)

An audit of the pages a stranger actually lands on, and the fixes it found. Not a scanner run —
a scanner reports contrast ratios and duplicate ids, and would not have caught any of this.

**No way past the navbar.** There was no skip link and no `<main>` landmark, so a keyboard user
tabbed the entire navigation again on every page and a screen-reader user had no “jump to main”
to jump to. That is WCAG 2.4.1 failed outright, and it is most of the reason people give up on
keyboard navigation. Both added; the skip link is off-screen until focused, then unmissable.

**Six unlabelled boxes for the OTP.** The download page's code gate rendered six identical
`<input>` elements with no accessible name: a screen reader announced “edit text” six times, and
the code was simply unenterable without sight. Each now says which digit it is, inside a named
group.

**Errors that were shown but not announced.** The password and OTP failures wrote into a
paragraph with no live-region role — visible to somebody looking at that exact spot, silent to
everybody else. `role="alert"` on both.

**English labels in a bilingual product.** “User menu”, “Menu” and “Legal” were hardcoded, so a
Vietnamese screen-reader user heard English regardless of the switcher. Localised, and the test
asserts the names follow the chosen language — which is the part that would silently regress.

Tests: `AccessibilityTests` (6) in the browser suite, because every one of these is a property of
the rendered page and a keyboard, not of a handler. Browser suite green at 42, server at 1009.

**Not shipped**: a full WCAG AA audit of the signed-in app (this batch covered the recipient page
and the shell every page inherits), contrast re-checks of the design tokens (`DESIGN.md` owns the
palette, and changing it is a design decision rather than a fix), and reduced-motion coverage
beyond what the download page already has.

## Tier 4 — batch 31: upload by email (#104)

Forward a mail to a private address, get the links back. For the case where opening the app and
re-uploading is three minutes of fighting a file picker on a phone.

**The address is not a secret and is not treated as one.** It travels in the headers of every
mail it appears in. What makes it safe is that only the account's own address may use it: anybody
can send, nobody but the owner gets a delivery out of it, and the reply only ever goes to the
owner. Mail `From` is forgeable, so this is a convenience boundary rather than a security one —
the worst a forged `From` achieves is a delivery in the owner's own list.

**Minted on first ask, never at signup.** An address nobody requested is a live ingest route
nobody watches; and it is stable afterwards, because one that changed would break every
forwarding rule set against it. With no inbound domain configured the feature reports itself
unavailable rather than handing out an address no mail server accepts.

**Attachments arrive as base64, not as URLs.** Fetching from an address somebody else controls
is the SSRF surface `RemoteUrlFetcher` exists to contain, and it would make ingestion depend on
that link still being alive.

**The webhook answers 200 to almost everything** — unknown address, stranger's `From`, nothing
attached — because a provider retries anything that is not a 200 and none of those are conditions
a retry fixes. Its own authentication is a constant-time shared secret, and with none configured
every call is refused.

Plan caps, quota, retention and expiry apply exactly as they do to a browser upload: nothing
about arriving by mail relaxes a limit.

Tests: `EmailIngestTests` (10). Suite green at 1019. Design:
[`docs/upload-by-email.md`](upload-by-email.md).

**Not shipped**: mail *bodies* as text shares (`#178` already does that better), forwarding into
a workspace folder (the mail carries no folder, and guessing from a subject line files somebody's
contract in the wrong place), per-account ingest rules, and DKIM/SPF checks — the account
boundary carries the safety here, not the envelope.

## Tier 4 — batch 32: the drop page, and the room (#16 remainder, #17)

The blocked half of `#16`, unblocked, and `#17` on top of it.

**The blocker was a question, and the answer is no.** Batch 5 paused the `/r/{code}` submission
page on “the E2E-encryption decision (key-in-URL-fragment) for request uploads”. There is no
decision to make: end-to-end encryption here works by keeping the key in the URL fragment, and a
stranger dropping a file has nobody to hand that key to — the person who asked for the file has
to be able to open it. So request and room uploads are **not** end-to-end encrypted, and the page
says so in a sentence rather than leaving somebody to assume otherwise.

**Nothing in the product could create a file request.** The endpoints shipped in batch 5, the
list screen shipped with them, and no UI ever called `POST /api/file-requests` — the same
API-only defect the batch-11 audit found in four other screens, still true one tier away. The
create form is now on the requests page.

**A room is a request with one difference**, and it is the one that matters: everybody with the
link sees what everybody dropped. A plain request shows a submitter nothing — two people who
both have the link are not thereby entitled to each other's files. Anything unrecognised in the
`kind` field is a request, because the private shape is the safe default.

**The QR is the room's actual interface** (#17): printed, taped to a wall, pointed at with a
phone. Rendered on demand from the QR library the send page already loads, not with the row.

An unknown, expired or closed code renders one generic unavailable state, so the page never
becomes an oracle for which codes exist.

Tests: `DropRoomTests` (8). Server suite green at 1027, browser at 42.

**Not shipped**: per-room moderation (removing somebody else's drop), submitter names on files
— a name typed by an anonymous stranger is a claim, and the room would present it as a fact —
and notification-per-drop, which is what `FileRequestDigestJob` already batches.

## Tier 4 — batch 33: PDPD consent (#230)

Vietnam's Decree 13 is the local law for the market this product sells into, and it asks for
something the GDPR work did not provide: consent that is **specific to transferring personal data
abroad**, which is exactly what our storage does.

**The mechanism is what is built, and the wording is what gets reviewed.** `docs/pdpd-compliance.md`
says so in its first paragraph. Shipping a consent flow while pretending the text has been through
a lawyer would be the worst of both.

**Versioning is the load-bearing part.** A consent means something only against the text that was
shown, so the version travels with the record and an agreement to last year's wording does not
count as agreement to this year's. Re-granting an active consent is a no-op — a log with an entry
per page load says nothing about when somebody actually agreed.

**Withdrawal marks the row rather than deleting it.** *Consented on Tuesday, withdrew on Friday*
is the record that matters; erasing the second half would erase the part that protects the person.

**The banner does not block anything.** Consent that is a condition of using the thing you already
paid for is not freely given. It is shown to accounts reading the site in Vietnamese, once per
version; locale is what we have, and it is not a claim about where somebody lives.

The 72-hour A08 breach branch is written into `ONCALL.md` as its own path, because it has a
different clock and a different recipient from the GDPR one.

Tests: `PdpdConsentTests` (6). Suite green at 1033.

**Not shipped**: a Vietnamese-hosted storage region (#119 makes one configurable; standing it up
is an operations decision with a cost), automated A08 notification (a human process with a form at
the end), consent for anonymous senders (nothing is stored about them beyond the delivery, and
asking a stranger to agree to a processing notice before they can send a file is consent theatre),
and a separate VN privacy page.

## Tier 4 — batch 34: manual folder order (#275)

The last open tier-4 item. Folders sorted alphabetically and nothing could change that, which is
fine until the tree is “Client A, Client B, Archive” and the archive keeps landing first.

**One request carries the whole arrangement**, not a sequence of moves. A reorder is a single
intention; applying it move by move means every intermediate state is a real state somebody could
load. Positions are assigned from the sequence rather than trusted from the caller, so a
duplicate or a gap cannot produce a list that sorts differently for the next reader than it did
for the person who dragged it.

**Name is still the tie-break.** Every folder that predates ordering sits at position zero, and
alphabetical is a better answer there than insertion order.

**Drag *and* buttons.** A reorder that only works by dragging is a reorder keyboard users cannot
do at all — which the accessibility batch two batches ago spent its time on. The rows are
draggable for a mouse and every row carries move-up/move-down buttons with real labels.

**An id from another workspace is silently not found**, which is the same answer as it never
existing.

Tests: `FolderOrderTests` (6). Server suite green at 1039, browser at 42.

**Not shipped**: reordering *deliveries* inside a folder (they sort newest-first, and a manual
order there would fight the cursor paging every list in the product uses), and cross-parent drag
— moving a folder under a different parent is a different operation from arranging siblings.

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
| Compliance (legal hold / retention / delivery scope) | `docs/compliance-architecture.md` |
| Team workspaces (seats / transfer / audit / report / folders) | `docs/workspaces-architecture.md` |
| Billing ledger (seats / overage / invoices / PO + net terms) | `docs/billing-ledger.md` |
| Enterprise trust (isolation / SIEM / DPA / SOC 2) | `docs/enterprise-trust.md` |
| Object storage & backup (S3-compatible / scheduled export) | `docs/object-storage-and-backup.md` |
| Workspace branding (recipient page / typography) | `docs/workspace-branding.md` |
| Delivery gating (approval / recurrence) | `docs/delivery-gating.md` |
| Comments & chat channels | `docs/comments-and-channels.md` |
| White-label domain & preview watermark | `docs/white-label.md` |
| Protocol mounts (WebDAV, S3 gateway) | `docs/protocol-mounts.md` |
| Voice commands | `docs/voice-commands.md` |
| OCR search | `docs/ocr-search.md` |
| Outlook add-in | `docs/outlook-addin.md` |
| Upload by email | `docs/upload-by-email.md` |
| PDPD (Vietnam) compliance | `docs/pdpd-compliance.md` |
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
- [x] Tier 4: complete (08-23). The last four were `#104` upload by email, `#16`/`#17` the drop
  page and rooms, `#230` PDPD consent and `#275` manual folder order.
- [ ] Tier 5: the remaining open tier; see the label filter.
- [ ] Tier 6: 40 of ~50 done — compliance batch `#266`/`#161`/`#142`/`#166` (08-20), team batch
  `#106`/`#252`/`#145`/`#300`, folder batch `#151`/`#245`/`#234` and billing batch
  `#278`/`#141`/`#112`/`#192` (08-21, see section 8); then `#323` SOC 2, `#289` comments,
  `#180` channels, `#162`/`#176` gating, `#285`/`#286` branding, `#119` residency, and the
  identity batches `#128`/`#181`/`#219`/`#146`/`#139` (12–13). Batch 19 closed `#81`
  white-label and `#155` preview watermark; batch 20 closed `#195` WebDAV and
  batch 21 `#265` S3 gateway and batch 22
  `#277` voice commands and batch 23 `#225` crypto pay and batch 24
  `#243` OCR search and batch 27 `#194` Outlook add-in. Still open: `#196` SFTP (no maintained
  .NET SSH *server* library — deferred rather than built on an archived one) and `#163` BYOK
  (needs the upload path to carry a customer-wrapped data key; thin is worse than none)
