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
| Tier 6 | B2B / enterprise / far-future | 32 done (08-22): `#289` delivery comments, `#180` Slack/Teams posting, `#162` approval workflow, `#176` recurring delivery, `#119` data residency, `#285` branded recipient page, `#286` custom fonts, `#171` S3-compatible storage, `#271` workspace backup, `#190` tenant isolation, `#191` SIEM streaming, `#184` self-serve DPA, `#147` SOC 2 readiness, `#323` SOC 2 roadmap, `#250` QR label sheet, `#282` announcement banner, `#308` file lock/checkout, `#266` legal hold, `#161` workspace retention policy, `#142` delivery IP allowlist, `#166` geo-restriction, `#106` team workspaces, `#252` ownership transfer, `#145` workspace audit log, `#300` admin usage report, `#151` shared folders, `#245` per-folder defaults, `#234` folder templates, `#278` team seats, `#141` Studio overage, `#112` e-invoice, `#192` PO + net terms |

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
for a setting changed once a year), and custom-domain-carried branding — `#67` is still open,
so `#81` white-label stays open too, since brand control without domain control is not
white-label.


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
- [ ] Tier 6: 32 of ~50 done — compliance batch `#266`/`#161`/`#142`/`#166` (08-20), team batch
  `#106`/`#252`/`#145`/`#300`, folder batch `#151`/`#245`/`#234` and billing batch
  `#278`/`#141`/`#112`/`#192` (08-21, see section 8); `#323` SOC 2, `#289` delivery comments,
  `#286` custom fonts, `#285` branded upload page, `#119` data residency, `#81` white-label,
  `#139` SAML SSO, `#146` SCIM, `#219` MFA policy (blocked: no MFA exists yet) and the rest
  still open
