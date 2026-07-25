# quickshare-daub-htmxor - Work Plan

## TL;DR (For humans)
<!-- Fill this LAST, after the detailed plan below is written, so it summarizes the REAL plan. -->
<!-- Plain English for a non-engineer: NO file paths, NO todo numbers, NO wave/agent/tool names. -->

**What you'll get:** QuickShareCloud migrated from RizzyUI + Alpine.js to DAUB (84 CSS components, 21 themes) + HTMXor. Same features, same backend — cleaner frontend, mobile-friendly UI, no Blazor component library dependency.

**Why this approach:** DAUB is a drop-in CSS class library (no Blazor wrappers to write), has explicit mobile support (bottom nav, drawer, responsive utilities), and 21 themes. HTMXor integrates HTMX natively with Blazor SSR — keeps .razor components, handles routing/antiforgery.

**What it will NOT do:** Change P2P WebRTC/WebSocket logic, change backend API, change auth, change file storage, rewrite working vanilla JS files (p2p/*.js, zapdrop-crypto.js).

**Effort:** Large (8 pages, 289 RizzyUI tags, 209 Alpine expressions to convert)
**Risk:** Medium — 6/8 pages have Alpine.js x-data that must be carefully converted to HTMX/vanilla JS
**Decisions to sanity-check:** 1) DAUB theme family choice, 2) Alpine → HTMX vs vanilla JS split per page

Your next move: review and approve this plan, or request changes. Full execution detail follows below.

---

> TL;DR (machine): Large effort, medium risk. 6-wave migration: infra setup → simple pages → medium pages → complex pages → P2P pages → cleanup. Each todo replaces RizzyUI components with DAUB CSS classes and Alpine.js with HTMX/vanilla JS.

## Scope
### Must have
- HTMXor NuGet + config (Program.cs, App.razor, _Imports.razor)
- DAUB CDN (daub.css + daub.js) with dark theme
- All 8 .razor pages converted from RizzyUI → DAUB CSS classes
- All Alpine.js x-data expressions converted to HTMX / vanilla JS
- upload.js Alpine component rewritten as vanilla JS
- Routes.razor RizzyUI components replaced
- Remove RizzyUI .csproj dependency, Alpine.js CDN, old compiled app.css
- Build passes, pages render correctly

### Must NOT have (guardrails, anti-slop, scope boundaries)
- Do NOT modify p2p/*.js files (signaling.js, peerConnection.js, fileTransfer.js, fileReceiver.js, encryption.js) — they are already vanilla JS
- Do NOT modify zapdrop-crypto.js — already vanilla JS
- Do NOT modify QuickShare.Server backend project
- Do NOT change auth middleware or OAuth flow
- Do NOT change database or file storage layer
- Do NOT rewrite P2P WebSocket signaling
- Do NOT add new features — only migration

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: tests-after — `dotnet build` + LSP diagnostics per file
- Evidence: .omo/evidence/task-<N>-quickshare-daub-htmxor.txt

## Execution strategy
### Parallel execution waves
| Wave | Todos | Description |
|------|-------|-------------|
| 1 | 1-3 | Infra setup (sequential — DAUB/HTMXor must exist before page migration) |
| 2 | 4-5 | Simple pages (parallel — Login + Pricing have minimal Alpine) |
| 3 | 6-8 | Medium pages (parallel — Account + Download + Routes.razor) |
| 4 | 9-10 | Complex pages (parallel — Files + Upload) |
| 5 | 11-12 | P2P pages (parallel — P2PTransfer + P2PDownload) |
| 6 | 13-14 | Cleanup (sequential — remove deps, build verification) |

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 (HTMXor NuGet) | — | 2, 3 | — |
| 2 (DAUB CDN) | 1 | 4-12 | — |
| 3 (HtmxorLayout) | 1 | 4-12 | 2 |
| 4 (Login) | 2, 3 | — | 5 |
| 5 (Pricing) | 2, 3 | — | 4 |
| 6 (Account) | 2, 3 | — | 7, 8 |
| 7 (Download) | 2, 3 | — | 6, 8 |
| 8 (Routes) | 2, 3 | — | 6, 7 |
| 9 (Files) | 2, 3 | — | 10 |
| 10 (Upload) | 2, 3 | — | 9 |
| 11 (P2PTransfer) | 2, 3 | — | 12 |
| 12 (P2PDownload) | 2, 3 | — | 11 |
| 13 (Remove RizzyUI dep) | 4-12 | — | — |
| 14 (Build verify) | 13 | — | — |

### DAUB Reference Docs
All component classes, skeletons, and JS API documented in `.omo/references/daub-llms.txt` (fetched from https://daub.dev/llms.txt).
Key mappings from RizzyUI → DAUB:
- RzButton → db-btn / db-btn--primary / db-btn--secondary / db-btn--ghost
- RzCard / CardContent → db-card / db-card__header / db-card__body / db-card__footer
- RzHeading → db-h1 / db-h2 / db-h3 / db-h4
- RzBadge → db-badge / db-badge--new / db-badge--warning / db-badge--error
- RzAlert → db-alert / db-alert--info / db-alert--warning / db-alert--error / db-alert--success
- RzSpinner → db-spinner / db-spinner--lg
- RzAccordion → db-accordion
- RzTabs → db-tabs
- RzModal → db-modal-overlay + db-modal (with DAUB.openModal())
- RzClipboard → DAUB.toast() for feedback + custom copy logic

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Install HTMXor + configure Program.cs + App.razor + _Imports.razor
  What to do / Must NOT do: 
  - `dotnet add package Htmxor --version 1.0.0-beta.1.24` in src/QuickShare.Web/
  - Add `.AddHtmx()` to `builder.Services.AddRazorComponents()` in Program.cs
  - Add `.AddHtmxorComponentEndpoints(app)` to `app.MapRazorComponents<App>()`
  - Add `app.UseHtmxAntiforgery()` after `app.UseAntiforgery()`
  - Add `<HtmxHeadOutlet />` before `<HeadOutlet />` in App.razor
  - Add `<body hx-boost="true">` to App.razor
  - Remove `<script src="_framework/blazor.web.js"></script>` from App.razor
  - Add `@using Htmxor.Components` / `@using Htmxor.Http` / `@using Htmxor` / `@using static Htmxor.Constants` to _Imports.razor
  - Add `@attribute [HtmxLayout(typeof(HtmxorLayout))]` to _Imports.razor
  Must NOT do: Do not remove other existing using directives in _Imports.razor
  Parallelization: Wave 1 | Blocked by: — | Blocks: 2, 3
  References: src/QuickShare.Web/Program.cs, src/QuickShare.Web/App.razor, src/QuickShare.Web/_Imports.razor; https://github.com/egil/Htmxor/blob/main/docs/index.md
  Acceptance criteria: `dotnet build` succeeds in src/QuickShare.Web/
  QA: bash `dotnet build` — 0 errors; evidence .omo/evidence/task-1-quickshare-daub-htmxor.txt
  Commit: Y | chore(deps): add HTMXor NuGet + configure Htmx services

- [ ] 2. Add DAUB CDN + configure theme in App.razor
  What to do / Must NOT do:
  - Add `<link rel="stylesheet" href="https://daub.dev/daub.css">` in App.razor `<head>`
  - Add `<script src="https://daub.dev/daub.js"></script>` before `</body>`
  - Set `data-theme="dark"` on `<html>` (or preferred DAUB theme)
  - Add anti-FOUC theme script (DAUB's localStorage snippet) before daub.css
  - Remove Alpine.js CDN (`<script src="https://unpkg.com/alpinejs"...`) from App.razor
  Must NOT do: Do not remove other scripts needed for P2P/crypto
  Parallelization: Wave 1 | Blocked by: 1 | Blocks: 4-12
  References: src/QuickShare.Web/App.razor; https://daub.dev/llms.txt
  Acceptance criteria: Page loads with DAUB-styled elements, dark theme active
  QA: Start dev server, verify no console errors; evidence .omo/evidence/task-2-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): add DAUB CSS + JS CDN with dark theme

- [ ] 3. Create HtmxorLayout.razor for HTMX direct routing
  What to do / Must NOT do:
  - Create src/QuickShare.Web/Components/Layout/HtmxorLayout.razor
  - Inherit `HtmxLayoutComponentBase`
  - Render `@Body`
  - No additional chrome (this is for HTMX partial responses only)
  Must NOT do: Do not add HeadOutlet or page chrome (HtmxLayoutComponentBase handles it)
  Parallelization: Wave 1 | Blocked by: 1 | Blocks: 4-12
  References: docs/index.md "Create an Optional Direct Request Layout" section
  Acceptance criteria: File exists, inherits HtmxLayoutComponentBase, builds
  QA: `dotnet build` — 0 errors; evidence .omo/evidence/task-3-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): add HtmxorLayout for HTMX direct routing

- [ ] 4. Convert Login.razor — RizzyUI → DAUB classes
  What to do / Must NOT do:
  - 14 RizzyUI components to replace: RzCard, RzHeading, RzButton, RzLink, etc.
  - Mappings: RzCard → db-card, RzHeading → db-h1/db-h2/db-h3, RzButton → db-btn db-btn--primary, RzLink → plain <a> with DAUB class
  - Login.razor has 0 Alpine.js — no HTMX conversion needed
  - Keep existing @code logic intact (redirect to Google OAuth)
  Must NOT do: Do not change OAuth URL or auth flow logic
  Parallelization: Wave 2 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/Login.razor; https://daub.dev/llms.txt
  Acceptance criteria: Login page renders with DAUB styling, OAuth redirect works
  QA: `dotnet build` + lsp_diagnostics on Login.razor; evidence .omo/evidence/task-4-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Login.razor to DAUB classes

- [ ] 5. Convert Pricing.razor — Alpine billing toggle → HTMX + DAUB
  What to do / Must NOT do:
  - 74 RizzyUI components: RzTabs, RzAccordion, RzCard, RzButton, RzBadge, RzHeading, RzParagraph, RzAuroraText, RzAlert
  - Mappings: RzTabs/TabsList/TabsTrigger/TabsContent → db-tabs (or db-segmented), RzAccordion → db-accordion, RzBadge → db-badge, RzAlert → db-alert, RzAuroraText → remove (decorative), RzSpinner → db-spinner
  - 13 Alpine expressions: `x-data="{ billing: 'monthly' }"` — convert billing toggle to HTMX hx-get with query param or simple vanilla JS toggle
  Must NOT do: Do not change pricing data or Stripe integration logic
  Parallelization: Wave 2 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/Pricing.razor; https://daub.dev/llms.txt
  Acceptance criteria: Pricing page renders with DAUB styling, billing toggle works
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-5-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Pricing.razor to DAUB + HTMX billing toggle

- [ ] 6. Convert Account.razor — branding Alpine → vanilla JS + DAUB
  What to do / Must NOT do:
  - 21 RizzyUI components: RzCard, RzHeading, RzButton, RzAlert, RzItem, RzBadge, RzSpinner, RzClipboard
  - 18 Alpine expressions: `x-data="brandingManager()"` inline Alpine component
  - Convert `brandingManager` Alpine object → vanilla JS class/functions
  - Keep existing account form validation logic
  - Mappings: RzClipboard/ClipboardTrigger/ClipboardFeedback → custom copy with DAUB.toast() feedback
  Must NOT do: Do not change account data logic (branding save, API keys)
  Parallelization: Wave 3 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/Account.razor; https://daub.dev/llms.txt
  Acceptance criteria: Account page renders, branding manager works, copy-to-clipboard works
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-6-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Account.razor to DAUB + vanilla JS branding

- [ ] 7. Convert Download.razor — downloadPage Alpine → HTMX + vanilla JS
  What to do / Must NOT do:
  - 37 RizzyUI components: RzCard, RzButton, RzHeading, RzAlert, RzSpinner, RzBadge, RzItem, RzClipboard
  - 34 Alpine expressions: `x-data="downloadPage()"` inline Alpine (~280 lines inline JS)
  - Convert downloadPage Alpine → vanilla JS functions
  - Mappings: RzClipboard → custom copy with DAUB.toast()
  - Keep zapdrop-crypto.js integration (already vanilla JS)
  - Keep file download logic intact
  Must NOT do: Do not modify zapdrop-crypto.js, do not change download decryption flow
  Parallelization: Wave 3 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/Download.razor; src/QuickShare.Web/wwwroot/js/zapdrop-crypto.js
  Acceptance criteria: Download page renders, file decryption + download works
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-7-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Download.razor to DAUB + vanilla JS download

- [ ] 8. Convert Routes.razor — RizzyUI components → DAUB
  What to do / Must NOT do:
  - Replace RzNotFound component inline markup with DAUB classes
  - Replace RzCard/RzHeading/RzButton with db-card/db-h*/db-btn
  - Keep Router/Auth/NotFound logic intact
  Must NOT do: Do not change routing logic or NotFound behavior
  Parallelization: Wave 3 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Routes.razor
  Acceptance criteria: Routes page builds, navigation works
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-8-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Routes.razor to DAUB classes

- [ ] 9. Convert Files.razor — filesPage Alpine → HTMX CRUD + vanilla JS
  What to do / Must NOT do:
  - 44 RizzyUI components: RzCard, RzButton, RzHeading, RzBadge, RzSpinner, RzItem, RzAlert, RzModal
  - 56 Alpine expressions: `x-data="filesPage()"` inline Alpine (~230 lines) + Alpine modals
  - Convert file listing CRUD to HTMX (hx-get for pagination, hx-delete for file delete, hx-post for rename)
  - Convert inline Alpine modals to DAUB modals (`DAUB.openModal()`)
  - Convert file type filtering to HTMX hx-trigger
  - Keep pagination logic — convert to HTMX partial page swaps
  Must NOT do: Do not change file storage backend, do not change file deletion/rename API
  Parallelization: Wave 4 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/Files.razor (1070 lines, most complex)
  Acceptance criteria: File list renders, pagination works, delete/rename via HTMX, modals work
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-9-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Files.razor to DAUB + HTMX CRUD

- [ ] 10. Convert Upload.razor + upload.js — Alpine → vanilla JS
  What to do / Must NOT do:
  - 33 RizzyUI components: RzCard, RzButton, RzHeading, RzAlert, RzSpinner, RzBadge
  - 36 Alpine expressions in Upload.razor: `x-data="uploadPage()"` references upload.js
  - **Rewrite upload.js** (477 lines, Alpine `x-data="uploadPage"`) as vanilla JS module
  - Keep: R2 Multipart Upload logic, client AES-256-GCM encryption, paste handler, drag-drop
  - Replace: x-data/x-show/x-bind/x-on/x-text → vanilla DOM manipulation + event listeners
  - Replace x-transition → CSS transitions or DAUB utilities
  - Mappings: RzSpinner → db-spinner, RzAlert → db-alert
  Must NOT do: Do not change upload flow, do not change R2 integration, do not change encryption
  Parallelization: Wave 4 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/Upload.razor; src/QuickShare.Web/wwwroot/js/upload.js
  Acceptance criteria: Upload page renders, drag-drop upload works, paste upload works, encryption works, progress bar shows
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-10-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert Upload.razor + upload.js to DAUB + vanilla JS

- [ ] 11. Convert P2PTransfer.razor — p2pSendPage Alpine → vanilla JS
  What to do / Must NOT do:
  - 39 RizzyUI components: RzCard, RzButton, RzHeading, RzAlert, RzBadge, RzSpinner, RzItem
  - 31 Alpine expressions: `x-data="p2pSendPage()"` inline Alpine (~300 lines)
  - Convert inline Alpine state management → vanilla JS
  - Keep p2p JS modules integration (signaling.js, peerConnection.js, fileTransfer.js, encryption.js)
  - Convert Alpine x-show/x-bind/x-on → vanilla DOM
  Must NOT do: Do NOT modify p2p/*.js files, do not change WebRTC/WebSocket flow
  Parallelization: Wave 5 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/P2PTransfer.razor; src/QuickShare.Web/wwwroot/js/p2p/*.js
  Acceptance criteria: P2P send page renders, file transfer works end-to-end
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-11-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert P2PTransfer.razor to DAUB + vanilla JS

- [ ] 12. Convert P2PDownload.razor — p2pDownloadPage Alpine → vanilla JS
  What to do / Must NOT do:
  - 27 RizzyUI components: RzCard, RzButton, RzHeading, RzAlert, RzBadge, RzSpinner
  - 21 Alpine expressions: `x-data="p2pDownloadPage()"` inline Alpine (~270 lines)
  - Convert inline Alpine state management → vanilla JS
  - Keep p2p JS modules integration (signaling.js, peerConnection.js, fileReceiver.js, encryption.js)
  - Convert Alpine x-show/x-bind/x-on → vanilla DOM
  Must NOT do: Do NOT modify p2p/*.js files, do not change WebRTC/WebSocket flow
  Parallelization: Wave 5 | Blocked by: 2, 3 | Blocks: —
  References: src/QuickShare.Web/Pages/P2PDownload.razor; src/QuickShare.Web/wwwroot/js/p2p/*.js
  Acceptance criteria: P2P download page renders, file download works end-to-end
  QA: `dotnet build` + lsp_diagnostics; evidence .omo/evidence/task-12-quickshare-daub-htmxor.txt
  Commit: Y | feat(ui): convert P2PDownload.razor to DAUB + vanilla JS

- [ ] 13. Remove RizzyUI dependency + old compiled CSS
  What to do / Must NOT do:
  - Remove RizzyUI NuGet package: `dotnet remove package RizzyUI` from QuickShare.Web.csproj
  - Remove `@using RizzyUI` from _Imports.razor
  - Remove RizzyUI service registration from Program.cs if present
  - Remove old compiled Tailwind CSS wwwroot/css/app.css if not referenced for other purposes
  - Remove Alpine.js related setup in App.razor (verify Alpine CDN already removed in task 2)
  Must NOT do: Do not remove other dependencies (OAuth packages etc.)
  Parallelization: Wave 6 | Blocked by: 4-12 | Blocks: 14
  References: src/QuickShare.Web/QuickShare.Web.csproj, src/QuickShare.Web/_Imports.razor, src/QuickShare.Web/Program.cs
  Acceptance criteria: `dotnet build` succeeds without RizzyUI package
  QA: `dotnet build` — 0 errors; evidence .omo/evidence/task-13-quickshare-daub-htmxor.txt
  Commit: Y | chore(deps): remove RizzyUI + Alpine.js dependencies

- [ ] 14. Final build verification + LSP diagnostics
  What to do / Must NOT do:
  - Run `dotnet build` on entire solution
  - Run lsp_diagnostics on ALL changed .razor files
  - Check for any remaining RizzyUI or Alpine references via grep
  - Verify dev server starts without errors
  Must NOT do: Do not make any code changes — verification only
  Parallelization: Wave 6 | Blocked by: 13 | Blocks: —
  References: All migrated files
  Acceptance criteria: `dotnet build` exit code 0, lsp_diagnostics 0 errors, no RizzyUI/Alpine references remain
  QA: `dotnet build` + lsp_diagnostics route/*.razor + Pages/*.razor; evidence .omo/evidence/task-14-quickshare-daub-htmxor.txt
  Commit: N | (verification only — no changes)

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit — verify all 8 pages converted, all RizzyUI/Alpine references gone
- [ ] F2. Code quality review — LSP diagnostics on all changed files
- [ ] F3. Build verification — `dotnet build` passes
- [ ] F4. Scope fidelity — confirm no P2P/crypto/backend changes were made

## Commit strategy
- Each todo commits atomically with descriptive prefix (feat/chore)
- Task 14 (verification only) does not commit
- Final commit strategy: one commit per page conversion, one commit for infra

## Success criteria
- All 8 .razor pages use DAUB CSS classes instead of RizzyUI components
- All Alpine.js x-data expressions removed — replaced with HTMX or vanilla JS
- upload.js rewritten without Alpine.js dependency
- p2p/*.js and zapdrop-crypto.js remain unchanged
- `dotnet build` passes with 0 errors
- No RizzyUI or Alpine.js references remain in the codebase
