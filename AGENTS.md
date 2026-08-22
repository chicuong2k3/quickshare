## Communication Mode

- **Caveman mode ON by default.** All agent responses MUST use caveman-full compression: drop articles, filler, hedging, pleasantries. Fragments OK. Short synonyms. Code blocks unchanged. Never self-reference or announce the style.
- **Auto-clarity for security warnings, destructive ops, ambiguous sequences, or when user asks to clarify.**
- **RTK (`rtk` prefix) cho MỌI lệnh bash.** `git status` → `rtk git status`. `dotnet build` → `rtk dotnet build`. Built-in tools (`read`, `grep`, `glob`) không qua bash hook — dùng `rtk read`/`rtk grep`/`rtk find` cho output lớn.
- **Pre-change impact check via slnmap MCP.** Trước khi sửa code: `find_symbol` → `find_usages` → `impact_analysis`.
- **Vietnamese input → Vietnamese caveman output.**

# QuickShareCloud — Agent Instructions

## HTMX Lazy Loading (DEFAULT — every page)

**Every page that has `OnInitializedAsync` MUST use this pattern.** The principle is simple:
- **Static data** (layout, headings, static markup) → returns immediately, no DB calls
- **Dynamic data** (DB queries, API calls) → fetched via HTMX after page load

The goal is instant page shell render (0ms) with zero blocking, then data fills in asynchronously via HTMX. This is NOT optional — it is the default architecture for all pages.

### Why this matters

| Approach | First paint | User experience |
|---|---|---|
| **HTMX lazy-load** (this rule) | < 100ms — skeleton instantly | Shell appears immediately, data fills in |
| `[StreamRendering]` | 200–500ms — still blocked by async init | Page "thinks" briefly, then streams |
| Plain `OnInitializedAsync` | 1–3s — blocked by DB queries | Page feels frozen, user clicks away |

`[StreamRendering]` still runs `OnInitializedAsync` in the initial render pass — it only helps stream the HTML AFTER async starts, but the first byte is still delayed. It is a fallback, not a solution.

### Pattern

1. **Shell render**: Check `HX-Request` header. If absent → return immediately. Render skeleton.
2. **Data fetch**: Skeleton uses `hx-get` + `hx-trigger="load"` + `hx-swap="outerHTML"`. On second request `HX-Request` is set → load data.
3. **All internal navigation**: Folder links, search, pagination, clear buttons — ALL use `hx-get` targeting the content area, NEVER plain `<a href>` (that causes full page reload).
4. **CSS for HTMX partials**: Scoped CSS (`.razor.css`) does NOT apply to HTMX responses from a different component. Use global styles in `wwwroot/css/app.css` with namespaced prefix (`account-`, `files-`, etc.).

```csharp
protected override async Task OnInitializedAsync()
{
    var hxHeader = HttpContextAccessor.HttpContext?.Request.Headers["HX-Request"].FirstOrDefault();
    if (hxHeader != "true") return; // shell render — show skeleton, no DB calls

    // ... data loading (DB queries, IMediator.Send, etc.) ...
}
```

```html
@* Shell skeleton — instant render *@
<div id="page-content"
     hx-get="/page-url"
     hx-trigger="load"
     hx-target="#page-content"
     hx-select="#page-content"
     hx-swap="outerHTML">
    @* skeleton markup *@
</div>
```
```html
@* All navigation links use HTMX *@
<a href="/page?filter=X"
   hx-get="/page?filter=X"
   hx-target="#page-content"
   hx-select="#page-content"
   hx-swap="outerHTML"
   hx-push-url="true">Filter X</a>
```

### Exceptions

- **Truly static pages** with zero async operations (pure markup, no DB/API calls) do not need lazy-load.
- `[StreamRendering]` is acceptable only when splitting shell + partial is impractical (complex interdependencies). Even then, prefer lazy-load.

### Layout Rule

- **MainLayout MUST inherit `LayoutComponentBase`** (not `HtmxLayoutComponentBase`). If MainLayout inherits `HtmxLayoutComponentBase`, HTMXor will treat it as a valid HTMX layout and use it for partial requests, returning full nav + footer.
- **HtmxorLayout inherits `HtmxLayoutComponentBase`** and renders only `@Body` — this is used automatically for HTMX partial responses via `[HtmxLayout(typeof(HtmxorLayout))]` in `_Imports.razor`.
- `@layout MainLayout` on individual pages is fine — `[HtmxLayout]` will override it for HTMX requests.

### HTMXor Direct Routing (.NET 10 compatibility)

- **`AddHtmxorComponentEndpoints(app)` is NOT compatible with .NET 10** — it uses reflection to access internal Blazor APIs that changed between .NET 8 and .NET 10. Calling it throws `NullReferenceException` at startup. Do NOT add it to `Program.cs`.
- **Without Direct Routing, ALL HTMX responses return full HTML pages** (including `<html>`, `<head>`, nav, footer from `App.razor`). The `[HtmxLayout]` attribute only affects the layout INSIDE the page pipeline; it does NOT strip the outer HTML document.
- **Workaround: always use `hx-select`** on every element that uses `hx-get`/`hx-post` + `hx-target`. This tells HTMX to extract only the targeted element from the full HTML response before swapping.
- Pattern:
  ```html
  <div id="my-content"
       hx-get="/page"
       hx-trigger="load"
       hx-target="#my-content"
       hx-select="#my-content"
       hx-swap="outerHTML">
  ```
- **Without `hx-select`, the full HTML document is injected into the target element**, causing duplicate nav/footer and broken layout.

## Code Style

- **One statement per line** — do not chain multiple statements on a single line. Each statement must be on its own line.
- **`string.Empty`, never `""`** — always use `string.Empty` instead of `""` for empty strings. Exception: `""` is required where the language mandates a compile-time constant (`const` declarations, attribute arguments, default parameter values, `switch` `case` labels) — everywhere else use `string.Empty`.
- **No magic strings / numbers** — never use hardcoded string/number literals directly. Always use `const` or `enum` (strongly typed). Protocol identifiers, HTTP headers, route paths, config keys, entity statuses, roles, and wire formats MUST be centralized in `static class` string constants (e.g. `AuthConstants`, `PlanTierLimits`, `P2PProtocol`, `RoutePaths`).
- **Directory membership is proved by the provider's claim, never by an email domain** (issue
  #181). `hd` / `tid` establish it; `AllowedEmailDomains` only ever narrows what the claim already
  established. Reversing that order hands a workspace seat to anyone who buys a convincing domain.
  A directory belongs to exactly one workspace, or the proof gets handed to the wrong tenant.
- **Sign a timestamp along with the body, never the body alone** (issue #172). A signature over
  just the payload makes a captured request valid forever and gives the receiver no way to tell a
  replay from the original.
- **An outbound integration is queued, never sent on the request path** (issue #39). It has to
  survive the receiver being down, which means a row and a retry schedule — unlike a best-effort
  chat post (#180), which documents the opposite trade for a reason that does not transfer.
- **An object key named by a client must be proved to belong to the resource claiming it**
  (issue #79). `R2ObjectKey.BelongsTo` exists because a commit request is attacker-controlled;
  without it a caller attaches somebody else's object to a share code they own.
- **Additive means the old case is untouched.** When adding a shape to an existing entity, keep
  the original columns meaning what they meant and test that the previous behaviour is identical —
  otherwise every path written before the change starts quietly lying.
- **Never hand-roll a signature check** (issue #139). XML digital signature in particular has a
  history of implementations that validate *a* signature rather than the one over the element they
  then read — a class of bug that is an authentication bypass and fails silently. Use the library,
  and keep ours to the questions a library cannot answer: which tenant, which issuer, seen before?
- **Prove a checker works by giving it something that should fail.** A validator that rejects
  everything passes every negative test; the positive case is what keeps the negatives honest.
- **A provisioning path must revoke as well as create** (issue #146). "What happens at
  offboarding" is the question the feature is bought to answer; a deactivate that returns 200 and
  leaves access live is the precise failure it exists to prevent.
- **Isolation CSS** — use scoped CSS (`.razor.css`) per component. JS is authored as TypeScript in `src/QuickShare/ts/` (do not use `.razor.js`). **`wwwroot/js/**` is `tsc` output and is gitignored** — a script written straight into it is absent from a fresh clone, so the page ships with dead buttons. Ten page scripts were in exactly that state until the audit follow-up; the shared helper they now use is `ts/page-actions.ts`. **Exception:** markup returned inside an HTMX partial does not carry the requesting component's scope attribute, so anything rendered that way must be styled from `wwwroot/css/app.css` with a prefixed global class (`account-`, `files-`, …).
- **User-facing strings live in `SharedLoc.{en,vi}.resx`** — never hardcode display copy in a page. Strings that JavaScript needs are handed to it as a JSON block rendered by the page (see `download-i18n`), so no `.js` file carries English literals.
- **Product analytics are anonymous** — new lifecycle events MUST be recorded via `AnalyticsService.RecordAsync()` (see `docs/analytics-architecture.md`). Never store email, user id, or raw IP in an analytics event; use the `qs_visitor` cookie id + `CF-IPCountry` code only. New event names go in `AnalyticsEvents` constants. Recording must never break the primary flow — wrap in try/catch or use the captured-context overload.

## Design System

`DESIGN.md` is the source of record for everything visible. Read it before writing CSS.

- **`--db-*` tokens are an override layer on the DAUB vendor stylesheet** (`https://daub.dev/daub.css`). Change *values*, **never names** — ~322 `db-*` class usages in markup depend on them.
- **Foil gold is a fill and never a text colour** (2.1:1 on the sheet). Typographic uses take `--db-color-accent-text`. Valley blue marks the active cell; mountain gray is everything at rest.
- Radius is zeroed at token level. The sheet's geometry is `skewX(-14deg)` with glyphs counter-skewed `skewX(14deg)`.
- Font sizes must come from the closed set in `DESIGN.md` (`typography.scale`). Anything outside it is a defect, not a choice.
- **The download page must work with no JavaScript**: the server renders a usable phase and JS only moves on from it. Never put `class="hidden"` back on the default phase — `DownloadNoScriptTests` will fail.
- Browser-only logic (clipboard, sessionStorage, JS phase switching) is covered by `BrowserAppFixture` + Playwright (see `PasteBrowserTests`), which runs the app as a real process and drives the installed Chrome rather than downloading a browser bundle.

## HTMX Usage

- **Never use HTMX for static data toggles** — if the data doesn't change server-side between states (e.g., billing monthly/yearly pricing, theme toggle, show/hide content), handle it client-side with vanilla JS or CSS classes. Server round-trips for static data are pure latency waste.
- **Prefer Blazor enhanced navigation over `hx-boost`** — Blazor SSR has built-in enhanced navigation since .NET 9. Do NOT add `hx-boost="true"` globally. Only use explicit `hx-get`/`hx-post`/`hx-trigger` on elements that genuinely need partial server updates (search, form submissions, real-time data).

## Architecture Rules

### Audit Fields (CreatedAt / UpdatedAt)

- **Never set `CreatedAt` or `UpdatedAt` manually in code.** The `AuditableEntityInterceptor` (registered in `AppDbContext`) automatically sets:
  - `CreatedAt = DateTime.UtcNow` for all added entities that have a `CreatedAt` property.
  - `UpdatedAt = DateTime.UtcNow` for all added or modified entities that have an `UpdatedAt` property.
- Entities inheriting from `BaseAuditEntity` get both. Custom entities like `Manifest` with only `CreatedAt` get partial support automatically via metadata discovery.

### FastEndpoints Authorization

- **Never check `HttpContext.User.Identity?.IsAuthenticated` manually.** FastEndpoints enforces auth by default — an endpoint without `AllowAnonymous()` already requires authentication.
- Always declare auth intent explicitly in `Configure()`:
  - `AllowAnonymous()` — for public endpoints (delivery download, webhook, payment callback, brand info).
  - **Nothing needed for authenticated-only endpoints** — FastEndpoints is secure by default. Just don't call `AllowAnonymous()`.
- **Exception:** Middleware that needs to distinguish anonymous vs. authenticated users (e.g. rate limiting) may read `IsAuthenticated` — but never use it as an auth gate.
- **Exception:** Dev auto-signin middleware in `Program.cs` may check `IsAuthenticated` to skip already-authenticated requests.

### Entity → DTO Mapping

- **Use Mapperly (`Riok.Mapperly`) for all entity-to-DTO mappings.** Place mappers in `Features/<Feature>/Shared/<Entity>Mapper.cs`.
- Pattern:
  ```csharp
  [Mapper]
  public static partial class XxxMapper
  {
      public static partial XxxResponse ToXxxResponse(this XxxEntity source);
  }
  ```
- **Exception:** LINQ `.Select()` projections that include server-side subqueries (e.g. `db.Files.Count(...)`) or computed expression fields are exempt — EF Core cannot translate Mapperly-generated methods to SQL. In those cases, construct the DTO inline within `.Select()`.
- **Exception:** Simple value-object responses (e.g. `new SomeResponse(sessionId, chunkSize)`) that don't map from a single entity do not require Mapperly.

### FusionCache (Data-Layer Caching)

- **All read-heavy data paths MUST use `CacheService` or `StaticCache.Instance`** — no direct DB queries without caching.
- `CacheService` (via DI) for Mediator handlers/endpoints. `StaticCache.Instance` for static classes and Razor inline blocks.
- **TTL guidelines:**
  - Download info: 30s (download count changes frequently)
  - File/folder lists: 15–30s (user-visible mutations)
  - User/billing data: 2–5min (profile/subscription changes)
  - Plan limits/config: 1h+ (rarely changes)
  - Ad tracking: 1min (per-ad refresh)
- **Invalidation:** Every mutation handler MUST invalidate affected cache keys after `SaveChangesAsync`. Use `cache.RemoveAsync(key, ct)`.
- **Kill switch:** Set `"FusionCache": { "Enabled": false }` in appsettings to bypass all caching (for testing/local dev).

### Background Jobs (Hangfire)

- **Recurring maintenance jobs use Hangfire, not hand-rolled `BackgroundService` loops.** Register them in `Infrastructure/Jobs/HangfireJobs.cs` with a stable job ID + cron. Each job class exposes one public entry method annotated `[DisableConcurrentExecution(timeoutInSeconds: 0)]` and `[AutomaticRetry(Attempts = 0)]`.
- **The Hangfire dashboard (`/hangfire`) MUST stay behind `AdminDashboardAuthorizationFilter`** — it is not under the ASP.NET Core auth pipeline. Do not remove or weaken the filter.
- **Mediator (martinothamar/Mediator) is the in-process request/response dispatcher** — source-generated `IRequestHandler` implementations, registered scoped (`AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped)` in `Program.cs`). Endpoints dispatch via `Resolve<IMediator>().Send<T>(cmd, ct)`. No durable messaging — background/polling jobs stay on Hangfire; do not reintroduce an outbox.
- **Gate all Hangfire wiring on `!useInMemoryDb`** so the in-memory/SQLite test factories never boot a Hangfire server or Postgres storage.

### Compliance Invariants (legal hold #266, retention policy #161, scope rules #142/#166)

See `docs/compliance-architecture.md` for the full subsystem. Three rules bind all future work:

- **Any new code path that deletes delivery rows or R2 objects MUST filter `LegalHoldAt == null`.** A hold outranks retention, the trash grace, the expiry sweep, and right-to-erasure — deleting held bytes is evidence loss during litigation. Held rows are *skipped*, not failed: a bulk operation reports them in `skipped` and continues. Single-target endpoints refuse with `ErrorCodes.LegalHoldActive` → 409.
- **Any new code path that computes a delivery expiry MUST pass it through `RetentionPolicy.Clamp`, after the plan cap.** The policy is a ceiling, never a floor — a sender's shorter choice always survives, and clamping is not a validation error.
- **Delivery scope rules (`DeliveryAccessPolicy`) are fail-closed.** A rule that exists refuses anyone it does not explicitly satisfy, and a malformed rule means "does not match" — never "matches everything". Rule snapshots may be cached per share code; a per-caller verdict MUST NEVER enter a shared cache.

### Workspace Invariants (team workspaces #106, transfer #252, audit #145, report #300, folders #151/#245/#234)

See `docs/workspaces-architecture.md` for the full subsystem. Three rules bind all future work:

- **Any new code path that writes `User.Plan` MUST re-push it to the owner's workspace seats** (`SubscriptionService.PushPlanToWorkspaceAsync` / `WorkspaceService.SyncMemberPlansAsync`). A seat borrows the owner's plan by copying it onto `User.Plan` so ~20 existing tier gates work unchanged; a plan write that skips the push leaves members on a plan nobody is paying for.
- **Any new upload path MUST go through `QuotaService`.** It resolves the workspace owner as the usage bucket, so pooling applies to check and record alike. Reading or writing `UserUsage` directly gives every seat a private, uncapped allowance.
- **Workspace data is admin-gated through one resolver** (`WorkspaceAuditReader.ResolveAdminSeatAsync`) and shared-history reads are read-only: every mutation path still matches on `SenderId`. Seeing a team-mate's share is not the same as being able to delete it.
- **Any new upload path that can target a folder MUST resolve `FolderDefaults.ResolveAsync` and enforce the result when `Locked`.** Unlocked folder defaults are a prefill the sender may override; locked ones are policy (expiry replaced, privacy forced on, commit refused without a password). An upload path that ignores a locked folder is a compliance rule with a hole in it.
- **Folder creation goes through `FolderWriter.CreateAsync`, and deleting a folder never deletes content.** That function owns the two-level depth cap, the per-workspace ceiling and sibling-name uniqueness; the delete guard refuses while any delivery (soft-deleted included) or child folder is still filed under it.

### Billing Invariants (seats #278, overage #141, e-invoice #112, PO/net terms #192)

See `docs/billing-ledger.md` for the full subsystem. Three rules bind all future work:

- **Every charge goes through `InvoiceService.AddOrUpdateLineAsync`, never a direct `InvoiceLine` insert.** That method owns the draft-per-period lookup (which checks the change tracker before the database), the overage line-rewrite rule, and the retotal. A charge written around it lands on a second draft and disappears from the issued invoice.
- **`IsExceeded` means blocked; `IsMetered` means allowed and billable.** Studio keeps uploading past its quota and pays per GB; Free and Pro still hard-block. Treating `IsMetered` as a block silently reinstates the hard stop for the tier that pays to avoid it.
- **Never mutate an issued invoice's totals or buyer details — issue a new one.** Issuing freezes a copy of the buyer's company, tax code and PO reference on purpose: an invoice has to keep saying what it said when it was issued.

### Trust Invariants (tenant isolation #190, SIEM streaming #191, DPA #184, SOC 2 #147/#323)

See `docs/enterprise-trust.md` for the full subsystem. Three rules bind all future work:

- **Tenant isolation is an explicit `Where` per query, never an EF global query filter, and every workspace-scoped route that takes an id MUST get a case in `TenantIsolationTests`.** A global filter is lost silently to `IgnoreQueryFilters`, raw SQL, or a projection built before it applies, and the failure mode is a cross-tenant read with nothing in the logs. That test suite is the actual control; probe with a real id from the other tenant, because a random GUID passes even with scoping removed.
- **A cross-tenant miss and a nonexistent id MUST return the same response.** The reach is recorded server-side (`CrossTenantAccessBlocked`) but never revealed, or the error code becomes an existence oracle. A miss on an id that belongs to nobody is deliberately not recorded — the signal is a burst, and noise drowns it.
- **Never edit published DPA terms or a `/soc2` control status in place to match what we wish were true.** Bump `DpaTerms.CurrentVersion` (existing signatures point at a version that must keep meaning what it meant); on the trust page, "partial" is the honest status for a control with no independent evidence behind it, and a page that drifts is worse than none because it is a claim somebody will check.

### Storage & Backup Invariants (S3-compatible store #171, scheduled backup #271)

See `docs/object-storage-and-backup.md` for the full subsystem. Three rules bind all future work:

- **The object store is any S3-compatible endpoint, configured under `ObjectStore`; the legacy `R2` section MUST keep binding as a fallback.** `ObjectStoreResolver.Resolve` is the single place that decides which section wins — an options rename that silently breaks existing deployments on upgrade is not a feature.
- **A backup manifest is metadata plus storage keys, never file bytes, and pruning happens only after a successful upload.** Deleting old copies on the strength of a run that produced nothing is how a retention policy deletes the last good backup. Adding workspace data a customer would need after a disaster means adding it to `BuildManifestAsync`, and changing the shape means bumping `ManifestVersion` — customers hold copies we cannot migrate.
- **Run-now MUST go through the same method as the scheduled run.** A "test backup" on a separate code path proves nothing about the one that runs at 3am. It returns 200 with the failure in the body when the destination rejects it: the request was fine, the destination was not, and that distinction is the answer the admin asked for.

### Branding Invariants (recipient-page brand #285, custom fonts #286)

See `docs/workspace-branding.md` for the full subsystem. Three rules bind all future work:

- **A brand overrides token *values* only — never token names, and never a type size.** `DESIGN.md` owns the typography scale; a brand swaps families. The CSS renderer emits no `font-size` at all, and the tests assert that, because it is the kind of constraint that erodes one convenient exception at a time.
- **Everything interpolated into brand CSS MUST be validated at the write boundary** (hex colours by regex, font family names against a restricted character set). That is what makes rendering it as a `MarkupString` safe; validating at render time instead would leave every future call site to remember.
- **Uploaded brand assets are sniffed by magic bytes, never by the declared content type, and SVG is refused.** SVG can carry script and a logo is not worth an XSS surface on a client's page. Replacing an asset MUST write a new key — the public route is immutably cached, so an in-place overwrite serves last month's logo.

### Residency Invariants (data residency #119)

See `docs/object-storage-and-backup.md` for the full subsystem. Three rules bind all future work:

- **The storage region lives in the object key, and routing is derived from it.** `InitR2MultipartUploadAsync` is the only method that takes a region, because it is the only call that creates a key. New code that builds an upload key MUST go through `R2ObjectKey.For` and pass the region; a hand-rolled key silently lands in the default bucket.
- **A key without a recognised region segment MUST keep resolving to the default bucket.** Every object written before residency existed has a user GUID in that slot; region codes are `^[a-z0-9]{2,12}$` precisely so a 32-char hex string can never match one. Changing the code pattern without checking that property breaks every existing object.
- **Never move stored bytes across regions as a side effect.** Changing the account setting applies to future uploads only, and replacing a file keeps the delivery's original region. Relocation is a copy, a verification and a delete — it is a deliberate operation, not a consequence of a settings save.

### Gating Invariants (approval workflow #162, recurring delivery #176)

See `docs/delivery-gating.md` for the full subsystem. Three rules bind all future work:

- **Any new path that publishes a delivery to recipients MUST check `PendingApproval` first.** The status column is not self-enforcing. At commit the recipient list is parked on `Delivery.PendingRecipients` and no mail goes out — not even the sender's own link mail — because a gate that mails the link and then asks permission is not a gate.
- **`PendingApproval` MUST be indistinguishable from not-found on every recipient-facing path.** Never add a distinct status code for it: whether a sender's organisation reviews its shares is a fact about that organisation, not about the recipient, and a separate code turns the endpoint into an oracle for internal process.
- **Any code that copies a delivery MUST carry `StorageRegion` and MUST NOT carry legal hold, approval state or `DeletedAt`.** A copy is a new share, not a clone of the original's compliance state. `CloneDeliveryHandler` was silently moving copies across jurisdictions until this batch; assume the next copy path will too unless it is written deliberately.

### Conversation Invariants (delivery comments #289, chat channels #180)

See `docs/comments-and-channels.md` for the full subsystem. Three rules bind all future work:

- **Comments are gated by five independent switches, all of which only close**: the sender's `Delivery.CommentsEnabled`, the `Workspace.CommentPolicy` ceiling, privacy mode (#187), the delivery's lifecycle (expired/revoked/suspended/awaiting approval), and its scope rules via `DeliveryAccessService` (#142/#166). Any new comment surface MUST resolve them through `CommentGate.EvaluateAsync`. Comments are a recipient-facing content path: a caller refused the file must be refused the conversation about it — this was missed on first delivery and is now the reason the rule is written this way.
- **A chat webhook URL is a credential.** Never return it from an endpoint, never put it in an audit payload (record the host), and post over HTTPS only. Channel posts are best-effort with a short timeout and MUST NOT be able to fail a share or a comment — a chat outage is not an upload error.
- **Test stubs MUST produce the same shapes as production, and every feature needs at least one test that proves it is *reached*.** The storage stub returned a made-up object key, which made an entire residency feature untestable while everything looked green; separately, three services were written, documented and never called by anything. A unit test proves a function computes the right answer and says nothing about whether anything calls it. Assert the wiring through an observable side effect — see `AuditRegressionTests`.

@RTK.md

## Keeping the GitNexus index fresh

Reindex with:

```bash
npx gitnexus analyze --skip-agents-md
```

`--skip-agents-md` stops the generated block below from being rewritten. Symbol and edge counts are **not stable** — re-running against the same commit produced 2324, 2349 and 2372 — so they have been removed from these files: they churn git on every index without changing how anyone writes code. (`--no-stats` is documented in `--help` but has no effect.)

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **quickshare**. Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## When Debugging

1. `gitnexus_query({query: "<error or symptom>"})` — find execution flows related to the issue
2. `gitnexus_context({name: "<suspect function>"})` — see all callers, callees, and process participation
3. `READ gitnexus://repo/quickshare/process/{processName}` — trace the full execution flow step by step
4. For regressions: `gitnexus_detect_changes({scope: "compare", base_ref: "main"})` — see what your branch changed

## When Refactoring

- **Renaming**: MUST use `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` first. Review the preview — graph edits are safe, text_search edits need manual review. Then run with `dry_run: false`.
- **Extracting/Splitting**: MUST run `gitnexus_context({name: "target"})` to see all incoming/outgoing refs, then `gitnexus_impact({target: "target", direction: "upstream"})` to find all external callers before moving code.
- After any refactor: run `gitnexus_detect_changes({scope: "all"})` to verify only expected files changed.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Tools Quick Reference

| Tool | When to use | Command |
|------|-------------|---------|
| `query` | Find code by concept | `gitnexus_query({query: "auth validation"})` |
| `context` | 360-degree view of one symbol | `gitnexus_context({name: "validateUser"})` |
| `impact` | Blast radius before editing | `gitnexus_impact({target: "X", direction: "upstream"})` |
| `detect_changes` | Pre-commit scope check | `gitnexus_detect_changes({scope: "staged"})` |
| `rename` | Safe multi-file rename | `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` |
| `cypher` | Custom graph queries | `gitnexus_cypher({query: "MATCH ..."})` |

## Impact Risk Levels

| Depth | Meaning | Action |
|-------|---------|--------|
| d=1 | WILL BREAK — direct callers/importers | MUST update these |
| d=2 | LIKELY AFFECTED — indirect deps | Should test |
| d=3 | MAY NEED TESTING — transitive | Test if critical path |

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/quickshare/context` | Codebase overview, check index freshness |
| `gitnexus://repo/quickshare/clusters` | All functional areas |
| `gitnexus://repo/quickshare/processes` | All execution flows |
| `gitnexus://repo/quickshare/process/{name}` | Step-by-step execution trace |

## Self-Check Before Finishing

Before completing any code modification task, verify:
1. `gitnexus_impact` was run for all modified symbols
2. No HIGH/CRITICAL risk warnings were ignored
3. `gitnexus_detect_changes()` confirms changes match expected scope
4. All d=1 (WILL BREAK) dependents were updated

## Keeping the Index Fresh

After committing code changes, the GitNexus index becomes stale. Re-run analyze to update it:

```bash
npx gitnexus analyze
```

If the index previously included embeddings, preserve them by adding `--embeddings`:

```bash
npx gitnexus analyze --embeddings
```

To check whether embeddings exist, inspect `.gitnexus/meta.json` — the `stats.embeddings` field shows the count (0 means no embeddings). **Running analyze without `--embeddings` will delete any previously generated embeddings.**

> Claude Code users: A PostToolUse hook handles this automatically after `git commit` and `git merge`.

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

## Issue Completion � Docs Update Rule (MANDATORY)

After EVERY issue implementation is complete (before closing the issue / before pushing), update ALL related documentation in this order:

1. **docs/*.md architecture/feature docs** � if the issue touches a documented flow (e.g. send-architecture.md, payment-architecture.md, p2p-architecture.md, login-architecture.md), update the doc to reflect the new behavior. If no existing doc covers the changed area, create one.
2. **docs/superpowers/specs/ and docs/superpowers/plans/** � the brainstorming spec + implementation plan produced by the workflow are the record of the change; make sure they are committed alongside the code.
3. **_bmad-output/ artifacts** � if a BMad workflow (specs, architecture, brainstorming) produced artifacts relevant to the issue's area, update the affected artifact(s) or note the change in them.
4. **DESIGN.md / design-system docs** � only if the issue changed visible UI (new fields, components, tokens). Record what changed and why.
5. **AGENTS.md conventions** � if the issue introduced a new pattern/constraint future work must follow, add it here.
6. **Onboarding / index docs** — the next human starts here; keep them in sync or onboarding breaks:
   - `docs/backlog.md` — feature inventory + tier status. Update on issue create/close or tier change.
   - `docs/DEVELOPER.md` — codebase map + conventions. Update on new `Features/<X>/` folder, project, or rule.
   - `docs/TESTING.md` — test map + regression matrix. Update on test suite add/rename/remove.
   - `docs/SETUP.md` — env vars + local setup. Update on new env var, service, or setup step.

Trigger → doc (quick map):

| Change | Update |
|---|---|
| New/closed GitHub issue | `docs/backlog.md` |
| New `Features/<X>/`, project, or convention | `docs/DEVELOPER.md` (+ `AGENTS.md` if a rule) |
| Test suite added/renamed/removed | `docs/TESTING.md` |
| New env var / service / tool version | `docs/SETUP.md` |
| New alert / service / failure mode | `docs/ONCALL.md` |

The update happens in the SAME commit series as the implementation (docs commit may be separate, but must land before the issue is marked done). Skipping docs = the issue is NOT complete.

## Feature Issue Testing Rule (MANDATORY)

Every feature issue MUST carry a **Manual Test Checklist** — it is the definition of done for that feature. No checklist = the issue is incomplete and MUST NOT be marked done.

### Option A — Checklist inside the feature issue (DEFAULT)

Write a `## Manual Test Checklist` section in the feature issue body BEFORE implementation starts:

```markdown
## Manual Test Checklist
- [ ] Step 1: <action> → <expected result>
- [ ] Step 2: <action> → <expected result>
```

Rules:
- One checkbox per scenario. Each step states BOTH the action and the expected result.
- Cover: happy path, validation errors, empty states, permission boundaries (guest/free/paid), and the no-JS / HTMX-partial path where relevant (see `DownloadNoScriptTests`).
- The checklist is written at issue creation time, not after implementation — it drives the work, it does not document it.

### Option B — Separate manual-test issue (LARGE / CRITICAL features only)

For features touching **auth, billing, payments, P2P**, or spanning **3+ modules**, create a linked manual-test issue in addition to the checklist:

- Title: `[Manual Test] <Feature title>`
- Body: the full step-by-step checklist (same format as Option A) plus environment notes (browser, account tier, seed data).
- The feature issue links TO the manual-test issue; the manual-test issue is closed by the tester after the feature ships.

### PR / Implementation obligations

When implementing a feature issue, the agent MUST:

1. Follow the checklist scenario by scenario while building — each step is a test the code must satisfy.
2. In the closing PR description, add a `## Manual Test Evidence` section: check off each step from the issue checklist and record the result (screenshots for UI changes, API response for backend changes, or a written pass/fail note).
3. Run the existing automated suite covering the touched area (unit/integration tests, `BrowserAppFixture` + Playwright) BEFORE closing — manual steps never replace automated coverage, they complement it.
4. A feature issue is NOT complete until every checklist item is checked with evidence. Skipping the checklist = the issue is NOT done.
