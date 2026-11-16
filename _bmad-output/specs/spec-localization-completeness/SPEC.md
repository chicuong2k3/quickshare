---
id: SPEC-localization-completeness
companions:
  - localization-gap-catalog.md   # file:line evidence per gap, consumed during execution
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Localization Completeness — QuickShare UI

## Why

A **mandate to meet** (project AGENTS.md) colliding with a **pain to solve**: every user-facing string must live in `SharedLoc.{en,vi}.resx` and no `.js` file may carry English literals, yet the audit found 10 razor files that bypass localization entirely and ~30 hardcoded English/`Vietnamese` literals in JS. The two resx files are healthy (434/434 keys, full parity) — the gap is code that ignores them. Worst cases ship the wrong language to real users: `Upgrade.razor` and `ToolsSendImagesUncompressed.razor` hardcode Vietnamese (EN visitors see Vietnamese), `PlanCatalog.cs` serves Vietnamese plan descriptions to every EN user, and `send.js`/`ad-component.js` overwrite server-rendered `Loc[]` text with English. The fix is a mechanical migration onto the existing key set plus the proven `download-i18n` JSON handoff pattern — no new infrastructure.

## Capabilities

- **CAP-1** — Localized P2P send/download pages.
  - **intent:** Users can send (`/p2p/send`) and receive (`/p2p/d/{code}`) files in their active UI language, both in server-rendered markup and in every JS-driven state message.
  - **success:** All static text on `P2PTransfer.razor`/`P2PDownload.razor` renders through `Loc[]` using the existing `p2p_send_*` / `p2p_download_*` keys; `send.js`/`download.js` contain zero UI-facing English string literals (quota banner, receiver status, error messages, button labels, 5 const message blocks all read from a per-page i18n JSON block); language switch re-renders both flows.
- **CAP-2** — Localized Upgrade page and plan data.
  - **intent:** EN users see English on `/upgrade` and on every surface fed by plan metadata (`@planSummary`, `@planDescription`, `SummaryCard`).
  - **success:** `Upgrade.razor` renders through the existing `upgrade_*` keys (no hardcoded Vietnamese); plan `Description`/`Summary` resolve via dynamic keys `Loc["upgrade_plan_desc_{plan}"]` / `Loc["upgrade_plan_summary_{plan}"]` (keys `upgrade_plan_desc_pro/studio` + `upgrade_plan_summary_pro/studio` exist in BOTH resx files); `PlanCatalog` keeps only `Display` (proper noun) + pricing config — no hardcoded description/summary data left. EN and VI users each see their own language.
- **CAP-3** — Localized Tools pages and footer navigation.
  - **intent:** `ToolsShareText`, `ToolsOnlineClipboard`, `ToolsSendImagesUncompressed` and the MainLayout footer tool links render in the active language.
  - **success:** New `nav_tools_*` + `tools_*_heading/lede/pastehint` keys added to BOTH resx files (EN+VI); the three Tools pages pass `@Loc[...]` into `ToolsLanding` params; MainLayout footer links use `Loc[]`; an EN session shows zero Vietnamese strings.
- **CAP-4** — JS English literals purged.
  - **intent:** No `.js` file carries user-visible English copy; all JS-driven UI text is injected from a per-page i18n JSON block rendered by the page.
  - **success:** `upload.js`, `ad-component.js`, `p2p/send.js`, `p2p/download.js`, `p2p/{signaling,peerConnection,fileTransfer,fileReceiver}.js` contain no UI-facing English string literals; every literal found in the catalog (upload errors, expiry label, QR fallbacks, ad button states, P2P quota/status/error messages) resolves through the page i18n block; a grep for UI literals in `wwwroot/js` returns only protocol-internal strings that never reach the DOM.
- **CAP-5** — Residual hardcodes swept from already-localized files.
  - **intent:** No `Loc[]`-using page still ships a hardcoded user-facing string.
  - **success:** Confirmed items fixed (AccountBilling " Plan" suffix, MainLayout footer 3 links) and an execution-time sweep of Download/Home/Pricing/Upload/Account*/Login finds zero remaining user-facing literals; `git grep` for `>[A-Za-z]{2,}<` text nodes and `'[A-Z][a-z]` JS literals comes back clean except for structural files (PageWrapper, HtmxorLayout, AdReward) and protocol-internal JS.

## Constraints

- Every user-facing string MUST live in `SharedLoc.en.resx` + `SharedLoc.vi.resx` (AGENTS.md); pages never hardcode display copy.
- No `.js` file may carry English literals — JS i18n is handed over as a JSON block rendered by the page (replicate `Download.razor`'s `<script id="download-i18n" type="application/json">@RawJson(Strings())</script>` + `download.js` reader).
- Resx files stay at full EN/VI parity — any new key (CAP-3) is added to BOTH files; no key removal without a usage audit.
- Key naming follows `{page_prefix}_{slug}`; parameterized copy uses `{0}` placeholders (e.g. `upgrade_page_title`).
- Server-rendered text that JS later overwrites is NOT enough — every JS `textContent` write must pull from the i18n block (CAP-1, CAP-4).
- Markup inside HTMX partials is styled from global `wwwroot/css/app.css` — localization work must not change component class names or HTMX selectors.

## Non-goals

- No new resx infrastructure, pluralization framework, or locale beyond the existing en/vi pair.
- No rework of `ToolsLanding`/`PageWrapper`/`HtmxorLayout`/`AdReward` — they are structural and stay untouched (ToolsLanding stays param-driven; call sites localize).
- No translation of protocol-internal JS error strings that never reach the DOM (signaling/peerConnection internals) — only strings surfaced through `p2pd/p2ps-*` message elements.
- No audit fields, caching, or performance work — localization text only.

## Success signal

Switch the site to EN and walk the six hardcoded surfaces — `/p2p/send`, `/p2p/d/x`, `/upgrade`, the three Tools pages, the footer links, plus an ad reward prompt, an upload completion, and a P2P error — and see zero Vietnamese or unlocalized English: every string resolves through `SharedLoc`, every JS message reads from the page i18n block, and both resx files remain at 100% key parity. A language switch from VI to EN re-renders all of it correctly.

## Assumptions

- Assumed both resx files stay the source of truth and 434-key parity is the baseline to preserve (verified by audit).
- Assumed `download-i18n` is the canonical JS handoff and should be replicated rather than introducing a new mechanism.
- Assumed plan display names ("Pro"/"Studio") are proper nouns and may stay as-is; only Description/Summary are localized.
- Assumed `PageWrapper`, `HtmxorLayout`, `AdReward` (structural) and `ToolsLanding` (stateless params) are non-issues — fix at call sites only.
- Assumed Tools pages + footer links need NEW resx keys (`nav_tools_*`, `tools_*`) since none exist today.

## Status

**COMPLETE — 2026-08-14.** Tất cả capabilities đã triển khai và verify.

- **CAP-1** — DONE (P2P send/download localize; send.js/download.js đọc i18n JSON block).
- **CAP-2** — DONE (Upgrade.razor qua `upgrade_*` keys; `PlanCatalog` giữ `Display` + pricing config, bỏ hardcoded Description/Summary; `BillingHistoryService` loc param + cache key culture-aware; `UserCacheInvalidator` evict en+vi; `PayOSReturnEndpoint` localize; +4 key `upgrade_feat_studio_4` + `payos_*`).
- **CAP-3** — DONE (3 Tools pages truyền `@Loc[...]` vào `ToolsLanding` params; MainLayout footer → `nav_tools_*`; 38 keys `tools_*` + `nav_tools_*` thêm EN+VI).
- **CAP-4** — DONE (Upload.razor `upload-i18n` JSON block; upload.js `t()` helper + purge 13 literals; L152 `Loc["upload_resume"]`).
- **CAP-5** — DONE (AccountBilling " Plan" suffix, billingPeriodLabel, MainLayout 3 links; AdComponent.razor dev button `Loc["ad_dev_simulate"]` + i18n init config; ad-component.js `t()` + purge 12 literals + showSuccess rebuild; sweep `@evt.EventLabel` → nguồn `BillingHistoryService.GetEventLabel` đã localize từ CAP-2+5, AccountDeliveries dùng `ExpiryLabel()` → `Loc["deliveries_no_expiry"]`).

**Verify cuối:** resx parity 542/542 (OnlyInEN=0, OnlyInVI=0); `dotnet build src\QuickShare\QuickShare.csproj` 0 errors (4 warnings pre-existing); grep gates: không literal user-facing trong razor/JS còn lại — JS innerHTML còn lại đều dynamic; `console.warn`/`reject('Ad failed to load: …')` là developer-facing, giữ.

**Điều chỉnh so với kế hoạch:** chỉ thêm **1 key mới** `ad_dev_simulate` (8 key ad dự kiến đã tồn tại sẵn với tên khác — `ad_consent_required`, `ad_loading`, `ad_unavailable`, `ad_verifying`, `ad_verification_failed`, `ad_verify_error`, `ad_p2p_unlocked`, `ad_try_again`). Drift 1GB/5GB trong resx: resx là nguồn chuẩn (không sửa). Còn residual: AccountBilling.razor L163 `billingPeriodLabel` (đã quyết định giữ).