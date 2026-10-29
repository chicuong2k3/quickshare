---
title: 'Migrate QuickShare static SSR from Htmxor/HTMX to Unpoly.Blazor'
type: 'refactor'
created: '2026-09-24'
status: 'in-progress'
baseline_commit: '6b5de78667e6a9a5f0a58956526b0d8f387956d4'
review_loop_iteration: 0
context:
  - 'AGENTS.md'
  - 'DESIGN.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** HTMX/Htmxor is embedded in application startup, dozens of lazy-loaded Razor pages, forms, polling, navigation, cache headers, TypeScript and tests. Replacing just scripts or attribute prefixes leaves pages blank, forms broken, or private fragments cacheable.

**Approach:** Replace HTMX/Htmxor with Unpoly.Blazor and Unpoly 3 across the project. Migrate each converted surface to Unpoly.Blazor.Shadcn components and tokens while preserving behavior, authorization, and no-JavaScript paths. Existing DAUB surfaces remain during the phased migration; do not mix DAUB and Shadcn inside one component.

## Boundaries & Constraints

**Always:** Keep routes bookmarkable and usable without JS, preserve authenticated/tenant-scoped data and cache safety, CSRF protection, 422 validation, redirects, no-JS download, static SSR, present CSS and localization. Maintain responsive first paint via Unpoly placeholders/deferred loading where compatible with no-JS; ensure server-rendered content exists for direct navigation without JS. Keep existing API JSON consumers intact. Observe existing consent, payment, workspace and file policies.

**Ask First:** If a route cannot support both no-JS usable content and instant shell without a new endpoint/negotiation mechanism, or if Unpoly.Blazor cannot support existing Razor event/form binding under .NET 10, pause and present the options before changing behavior.

**Never:** Rename legacy `--db-*` tokens used by unmigrated surfaces, blindly replace `hx-*` with `up-*`, leave a hidden HTMX dependency, drop protection on mutating endpoints, or treat `X-Up-Target` as trusted authorization input.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Direct entry / JS off | Guest or signed-in GET | Complete usable route; authorization and download continue | Correct login/not-found response |
| Unpoly navigation | Link, search, tab, pagination, infinite scroll | Intended fragment updates; URL/history and focus make sense; no duplicate chrome | Error remains visible, not silent |
| Lazy data / poll | Authenticated account/dashboard GET | Fresh scoped data, no infinite requests, skeleton only during fetch | Clear retry or readable failure |
| Mutation | Profile multipart update, admin action, checkout status | CSRF-checked data persists once; valid success or redirect, invalid form stays visible | 422 for validation; no write on validation-only request |
| Cache boundary | Public vs personal GET with varying target/context | Proper `Vary`, public only where safe; private `no-store` | No cross-user fragment reuse |

</frozen-after-approval>

## Code Map

- `src/QuickShare/QuickShare.csproj`, `Program.cs`, `App.razor`, `_Imports.razor` — package, middleware, head wiring, global imports.
- `src/QuickShare/Components/Layout/{MainLayout,HtmxorLayout}.razor` — page chrome and fragment routing.
- `src/QuickShare/Components/Pages/**/*.razor`, `Components/Shared/*.razor` — approximately 54 files reference HTMX; covers skeletons, forms, polling, admin actions, fragment-only routes.
- `src/QuickShare/Infrastructure/{Constants/HtmxHeaders.cs,Middleware/HtmxCacheMiddleware.cs}` — protocol and public/private caching.
- `src/QuickShare/ts/{page-actions,globals,account,ad-component,download,upload}.ts` — client events and DOM actions.
- `tests/QuickShare.{Server,Browser}.Tests/` — HX headers, Htmxor form binding, browser smoke and no-JS coverage.

## Tasks & Acceptance

**Execution:**
- [ ] `vendor/Unpoly.Blazor.Shadcn/`, `src/QuickShare/tailwind.css`, `package.json`, `QuickShare.csproj`, `App.razor` — vendor the pinned component source, build Tailwind CSS, wire required runtime assets, and adopt Shadcn components per converted surface.
- [ ] `src/QuickShare/QuickShare.csproj`, `Program.cs`, `App.razor`, `_Imports.razor`, `Components/Layout/*` — wire supported Unpoly.Blazor version and `UseUnpoly` before antiforgery, swap head/chrome while keeping `HeadOutlet`, drop Htmxor when safe.
- [ ] `src/QuickShare/Components/Pages/**/*.razor`, `Components/Shared/*.razor` — inventory and migrate all HTMX consumers by interaction type; use real links/forms and documented Unpoly attributes/server headers, preserve no-JS paths and accessible status.
- [ ] `src/QuickShare/Infrastructure/`, `src/QuickShare/ts/` — migrate request detection, response events/redirects, cache Vary, antiforgery and TypeScript refresh hooks; remove dead HTMX wiring.
- [ ] `tests/QuickShare.Server.Tests/`, `tests/QuickShare.Browser.Tests/` — migrate protocol integration tests and cover direct GET, fragment fetch, form/validation, polling, auth/cache isolation, JS and no-JS.
- [ ] `AGENTS.md`, `docs/` (architecture, onboarding, testing, setup), `docs/superpowers/` and applicable `_bmad-output/` — replace obsolete HTMX conventions and document Unpoly behavior; leave DAUB design docs intact.

**Acceptance Criteria:**
- Given a clean clone, when dependencies restore and app starts, then no HTMX/Htmxor runtime package, markup, calls or headers are required.
- Given disabled JavaScript, when user opens a supported route, then core content/actions render and work without an empty permanent skeleton.
- Given Unpoly client, when user performs navigation, searches, mutations and polls, then results render in the intended target without duplicate chrome or unauthorized data.
- Given a converted surface, when migration finishes, then it uses Shadcn components/tokens and Unpoly behavior without legacy DAUB classes; unmigrated surfaces keep their existing CSS contracts until converted.

## Spec Change Log

- 2026-10-27: User expanded migration scope to include Unpoly.Blazor.Shadcn. Vendored upstream source at commit `134da689c0b7a178fea7ab657d189e433fe389fa`, added Tailwind v4 build integration, and began replacing migrated delivery controls with Shadcn components.

## Design Notes

Decision 2026-09-24: user selected A — initial GET renders full usable data even without JS; slower initial SSR accepted. This resolves the first-paint/no-JS tradeoff in the frozen Ask First boundary.

Progress 2026-09-24: added Unpoly.Blazor 0.1.0 as opt-in parallel runtime; migrated extension-request page and notification inbox to full SSR. Inbox filters, load-more and read-all refresh use Unpoly; new server and browser tests verify the actual swaps and no-JS route. Installed Playwright Chromium locally. HTMX/Htmxor remain required on other routes, so acceptance criteria and checkboxes remain open. Targeted server tests passed (31); browser migration + smoke tests passed (8). The browser read-all success test stubs the API because EF InMemory does not support `ExecuteUpdateAsync`; the real API passes its SQLite server test. The read-all control now has an authenticated POST form fallback with manual antiforgery validation and a redirect; server tests for valid/missing tokens were added but remain unverified: xUnit test discovery has begun hanging before executing any test, including unrelated tests. Build passes. Full server suite previously timed out with a NotificationJobTests failure that passed in isolation.

Unpoly.Blazor requests full SSR pages and selects fragments in the browser; its `UpChrome` skips chrome only for targeted requests. `UnpolyHead` supplies antiforgery. Use `up-target` rather than `hx-select`/`hx-target`; `up-defer="reveal"`, `up-poll`, `up-autosubmit`, and server `UpEmit`/`UpLocation` have different semantics than their HTMX counterparts. Verify library compatibility and exact APIs from upstream skill/docs before edits. Project instructions currently mandate HX-Request lazy loading and must be amended as part of this migration.

## Verification

**Commands:**
- `dotnet build QuickShare.slnx` — compiles without Htmxor.
- `dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj` — server flows pass.
- `dotnet test tests/QuickShare.Browser.Tests/QuickShare.Browser.Tests.csproj` — browser flows pass (Chrome prerequisite).
- `rg 'hx-|HX-Request|Htmxor|window\.htmx' src/QuickShare` — no active runtime references.
