---
target: Files page (src/QuickShare/Pages/Files.razor)
total_score: 24
max_score: 40
na_heuristics: 
p0_count: 2
p1_count: 2
timestamp: 2026-08-08T07-46-47Z
slug: src-quickshare-pages-files-razor
---
# Impeccable Critique — Files page (src/QuickShare/Pages/Files.razor)

Method: dual-agent (A: AssessmentADesign · B: AssessmentB)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | Skeleton + button spinners + toasts good; 'Storage used' stat sums only current 50-page; htmx swaps drop focus, no aria-live |
| 2 | Match System / Real World | 2 | Plain vocabulary but page hardcoded English in a vi-market app that localizes elsewhere; FormatDate hardcodes 'MMM d, yyyy' |
| 3 | User Control and Freedom | 2 | hx-push-url back/forward OK; no undo anywhere; no Escape on modals; drag-only move irreversible |
| 4 | Consistency and Standards | 2 | Tokens coherent but 40+ inline styles, hand-rolled modal CTAs, three button languages; theme 'grunge-dark' = light cream (comment admits); contradictory font comments |
| 5 | Error Prevention | 3 | Strong delete confirm + warning + danger red; gaps: empty folder name silent no-op; rename opens empty (no prefill); no type-to-confirm |
| 6 | Recognition Rather Than Recall | 3 | Type icons, count badges, active pill inversion; no breadcrumb; rename demands recall; fragile inline quote-escape |
| 7 | Flexibility and Efficiency | 2 | 500ms debounced search + drag-to-folder; no multi-select, bulk, shortcuts, pagination, sort |
| 8 | Aesthetic and Minimalist Design | 3 | Editorial cream/terracotta/serif distinctive; 4 stats + pill strip + 50 rows dense, no skimming aids |
| 9 | Error Recovery | 3 | error-card Retry, empty CTA, clear-search, toast errors; silently wrong Storage; session expiry drops to bare /login |
| 10 | Help and Documentation | 1 | Zero help for novel concepts (auto-expiry, quota); no drag affordance hint |
| **Total** | | **24/40** | **Acceptable — significant improvements needed** |

## Design Specificity Verdict

Brand skin, generic composition. Visual language is product-distinctive but lives in global daub tokens shared by every page (cream #EFE6D6 bg, ink #1C1712, terracotta #7A5C34, Playfair Display over Outfit — verified live). Files page contributes zero Files-specific signature: 4 stat cards, pill filter, row list, 3 modals — swap copy to 'Invoices' and composition is unchanged. The product's own promises (auto-expiry 'Free transfers auto-expire after 2 hours', quota headroom, share/relink) never appear; the app's signature mono readout (.qs-readout/.qs-eyebrow) is not used here.

**Deterministic scan (Assessment B)**: 0 findings, exit 0, pipeline controls verified (HTML low-contrast + CSS gradient-text probes both fired). Caveat: `.razor` not in SCANNABLE_EXTENSIONS so markup was not statically scanned — only scoped CSS files; genuine clean on those.

**Visual overlays**: not available. Injection target /__impeccable__/detect.js is 404 on the .NET server; mutation channel verified (title write succeeded), so a live overlay is technically possible but the injectable endpoint does not exist. /files auth-gated (→/login?ReturnUrl=%2Ffiles); live Files page not renderable anonymously. Homepage + login at 1440x900 and 390x844 DOM-measured; screenshots saved.

## Overall Impression

A branded skin over an interchangeable file manager. The cream/serif/terracotta system is genuinely distinctive, and the state-suite discipline (skeleton/error/empty/loaded) is professional. But the page fails the product's own pitch: shelf-life, quota, and share never surface, the storage ledger lies past 50 files, and there is no way past 50 rows at all.

## What's Working

- **State suite complete**: skeleton (4 stat cards + 4 skel-rows), error-card w/ Retry, empty-card w/ 'Upload a file', folder-empty inline CTA — every RenderState designed.
- **Selected-state design**: .folder-pill--active ink-inversion is the clearest signal on the page.
- **Accessibility hygiene where present**: aria-labels on all icon-only controls, role=list + aria-label on lists, prefers-reduced-motion, data-ai-action hooks.

## Priority Issues

- **P0 — 'Storage used' stat is numerically wrong**: Files.razor `new UsageStats(TotalCount, FileItems?.Sum(f => f.Size) ?? 0, …)` sums only the current 50-row page. Fix: aggregate SUM(size) server-side or real Usage endpoint; at minimum label 'in this view'.
- **P0 — 50-file cap with no pagination**: PageSize=50, footer 'Showing 50 of 123' then nothing; rest unreachable. Fix: next/prev or page numbers in .file-footer via hx-get + hx-push-url, honoring folderId/search (GetFolderUrl pattern).
- **P1 — Folder rename/delete invisible and unusable on touch/keyboard**: .action-btn {opacity:0} revealed only on .folder-pill-wrap:hover — no touch hover, no :focus-visible (tab hits invisible buttons), 20px targets, 0.125rem gap. Fix: always-visible ≥44px targets <640px; add :focus-visible; widen gaps.
- **P1 — Move-to-folder is drag-drop only**: file-row draggable + filesPage.onDrop PATCH /api/files/{id}/folder; no menu/button — touch + keyboard cannot move. Fix: per-row 'Move to…' folder picker; DnD as accelerator.
- **P2 — Auto-expiry/retention never surfaced**: rows show size·type·date only; core differentiator invisible; no share/relink per row. Fix: mono expiry pill (qs-readout) + per-row share action.
- **P2 — Modals lack dialog semantics + focus**: no role=dialog/aria-modal/aria-labelledby, no autofocus, no focus trap, no Escape (files.js zero keydown), no focus return. Fix: dialog roles, autofocus, Esc handler, focus return.
- **P3 — PageTitle mojibake**: 'Files â€” QuickShareCloud' misencoded em-dash in tab title. Fix: proper — entity.

## Persona Red Flags

- **Alex (power user)**: silent 50-cap no pagination; no multi-select/bulk; drag-only move; no shortcuts; upload on '/'.
- **Sam (accessibility)**: hover-only folder actions (no :focus-visible → tab hits invisible buttons); drag-only move no keyboard alt; no aria-live on list swaps; modals no role/trap/Esc; ~3:1 icon-button contrast; dark-on-dark folder-count badge.
- **Casey (mobile)**: measured 390px — navbar hamburger off-screen (x≈398), nav items collapse to 0-size, homepage overflow scrollWidth 786; folder actions invisible (no touch hover); 20px targets.
- **Jordan (first-timer)**: empty-state CTA leaves the page ('Upload a file' → /); rename opens empty demanding recall; 'Plan: Free' without quota/expiry context; 'New' vs 'New Folder' drift.

## Minor Observations

- All-Files pill icon (rect+circle+polyline) vs folder-pill folder glyph — inconsistent vocabulary in one row.
- Skeleton reuses .account-skeleton-line; h1+subtitle inline styles duplicated in skeleton/loaded branches.
- :root declares Sora, [data-theme=grunge-dark] overrides Playfair under a comment claiming 'Outfit for EVERYTHING'; theme name 'grunge-dark' renders cream ('Kira light').
- Search input loses focus mid-typing (htmx outerHTML swap replaces it, 500ms debounce).
- No aria-live on list count changes.
- FormatDate ignores culture.
- Folder names apostrophe-escaped inline into Alpine x-data.
- file-row draggable on a row holding a download link — accidental drags possible.

## Provocative Questions

- If the pitch is 'auto-expiring links', why does the inventory show shelf-life nowhere and a storage number that lies past 50 files?
- Every folder row has 5 affordances, every file row exactly 1 (download) — what would the row look like if 're-share + copy link' were the hero action?
- On 390px the navbar disappears and folder actions go invisible — Casey-first means which: bottom tabs, or content-first header rebuilding the nav?
