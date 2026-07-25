---
slug: quickshare-daub-htmxor
status: drafting
intent: clear
pending-action: write .omo/plans/quickshare-daub-htmxor.md
approach: Phased migration — infrastructure first, then pages from simple to complex (Login → Pricing → Account → Download → Files → Upload → P2PTransfer → P2PDownload). Each phase: replace RizzyUI components with DAUB CSS classes, replace Alpine.js with HTMX/vanilla JS, verify.
---

# Draft: quickshare-daub-htmxor

## Components (topology ledger)
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
<!-- id | outcome (one line) | status: active|deferred | evidence path -->
1 | HTMXor infra setup (NuGet, Program.cs, App.razor, _Imports) | active | docs/index.md
2 | DAUB CDN integration + theme setup | active | daub.dev/llms.txt
3 | Login.razor conversion (no Alpine, static) | active | src/QuickShare.Web/Pages/Login.razor
4 | Pricing.razor conversion (minimal Alpine billing toggle) | active | src/QuickShare.Web/Pages/Pricing.razor
5 | Account.razor conversion (branding + account forms) | active | src/QuickShare.Web/Pages/Account.razor
6 | Download.razor conversion (downloadPage Alpine) | active | src/QuickShare.Web/Pages/Download.razor
7 | Files.razor conversion (filesPage Alpine + CRUD) | active | src/QuickShare.Web/Pages/Files.razor
8 | Upload.razor conversion (upload.js Alpine rewrite) | active | src/QuickShare.Web/Pages/Upload.razor + wwwroot/js/upload.js
9 | P2PTransfer.razor conversion (p2pSendPage Alpine) | active | src/QuickShare.Web/Pages/P2PTransfer.razor
10 | P2PDownload.razor conversion (p2pDownloadPage Alpine) | active | src/QuickShare.Web/Pages/P2PDownload.razor
11 | Routes.razor RizzyUI cleanup | active | src/QuickShare.Web/Routes.razor
12 | Remove RizzyUI dep + Alpine.js CDN + old CSS | active | QuickShare.Web.csproj, App.razor, wwwroot/css/app.css

## Open assumptions (announced defaults)
<!-- Record any default you adopt instead of asking, so the user can veto it at the gate. -->
<!-- assumption | adopted default | rationale | reversible? -->
DAUB theme family | Default (warm cream) with dark mode via data-theme="dark" | QuickShare currently uses dark theme, DAUB default has light+dark | Yes — any DAUB family works
HTMXor beta version | 1.0.0-beta.1.24 | Latest stable beta on NuGet, supports .NET 10 | Yes — can upgrade later
P2P JS files stay as-is | Keep wwwroot/js/p2p/*.js unchanged | Already vanilla JS, no Alpine dependency | N/A
zapdrop-crypto.js stays as-is | Keep wwwroot/js/zapdrop-crypto.js unchanged | Already vanilla JS | N/A
Alpine.js → HTMX for forms | Form submissions via hx-post/hx-put with hx-target | HTMXor handles Antiforgery natively | Yes
Alpine.js → vanilla JS for state | Convert x-data components to vanilla JS singletons | DAUB JS handles modals/toasts, rest is custom | N/A
Pagination in Files.razor | HTMX hx-get with hx-target swap for page navigation | HTMXor direct routing supports partial renders | Yes

## Findings (cited - path:lines)
<!-- I'll fill these with evidence from our research -->
- Current project: .NET 10 Blazor SSR, 8 .razor pages, src/QuickShare.Web/
- RizzyUI components used: RzLink, RzCard, RzHeading, RzParagraph, RzButton, RzThemeProvider, RzSpinner, RzBadge, RzAlert, RzAccordion, RzTabs, RzClipboard, RzItem, RzAuroraText (14 components)
- Alpine.js on 6/8 pages: Upload.razor (upload.js), Download.razor (inline), Files.razor (inline), P2PTransfer.razor (inline), P2PDownload.razor (inline), Account.razor (inline), Pricing.razor (minimal)
- JS files: upload.js (477 lines, Alpine), p2p/*.js (5 files, vanilla), zapdrop-crypto.js (92 lines, vanilla)
- DAUB: 84 components, CSS classes (db-btn, db-card, etc.), 21 themes, CDN drop-in, JS for interactive (modal/toast/drawer)
- HTMXor: NuGet package, .AddHtmx() / .AddHtmxorComponentEndpoints(), HtmxHeadOutlet, direct routing, conditional rendering
- Auth: Cookie auth + Google OAuth, AuthPageRedirectMiddleware protects pages

## Decisions (with rationale)
1. DAUB over Bronto UI: DAUB has explicit mobile support (bottom-nav, drawer, responsive utilities), 21 themes, smaller JS, no build step
2. HTMXor for HTMX integration: Native Blazor SSR integration, handles routing/antiforgery, keeps .razor components
3. Keep existing vanilla JS files: p2p/*.js and zapdrop-crypto.js are already Alpine-free
4. Convert upload.js from Alpine to vanilla JS: It's the only .js file with Alpine dependency
5. Convert inline Alpine in pages to HTMX + vanilla JS: HTMX for forms/partial renders, vanilla JS for complex state

## Scope IN
- Install HTMXor NuGet, configure Program.cs + App.razor + _Imports.razor
- Add DAUB CDN links to App.razor, remove RizzyUI + Alpine.js CDN
- Convert ALL 8 .razor pages: RizzyUI → DAUB classes, Alpine → HTMX/vanilla JS
- Convert upload.js from Alpine to vanilla JS
- Update Routes.razor to use DAUB classes
- Clean up: remove RizzyUI .csproj dependency, remove Alpine.js, remove old compiled Tailwind CSS
- Choose and configure DAUB theme (dark mode)

## Scope OUT (Must NOT have)
- Do NOT rewrite P2P signal.js / peerConnection.js / fileTransfer.js / fileReceiver.js / encryption.js (already vanilla)
- Do NOT rewrite zapdrop-crypto.js (already vanilla)
- Do NOT change backend QuickShare.Server API
- Do NOT change auth logic (Cookie auth + Google OAuth works fine)
- Do NOT change database/file storage layer
- Do NOT rewrite P2P WebSocket signaling logic

## Open questions
<!-- All answered by research — no open questions remain -->

## Approval gate
status: awaiting-approval
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
Presented for review. Plan file written at .omo/plans/quickshare-daub-htmxor.md (14 todos across 6 waves).
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
