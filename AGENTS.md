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

    // ... data loading (DB queries, Bus.InvokeAsync, etc.) ...
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
- **Isolation CSS** — always use scoped CSS (`.razor.css`) per component. JS goes into `wwwroot/js/` (do not use `.razor.js`).

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
- `CacheService` (via DI) for Wolverine handlers/endpoints. `StaticCache.Instance` for static classes and Razor inline blocks.
- **TTL guidelines:**
  - Download info: 30s (download count changes frequently)
  - File/folder lists: 15–30s (user-visible mutations)
  - User/billing data: 2–5min (profile/subscription changes)
  - Plan limits/config: 1h+ (rarely changes)
  - Ad tracking: 1min (per-ad refresh)
- **Invalidation:** Every mutation handler MUST invalidate affected cache keys after `SaveChangesAsync`. Use `cache.RemoveAsync(key, ct)`.
- **Kill switch:** Set `"FusionCache": { "Enabled": false }` in appsettings to bypass all caching (for testing/local dev).

@RTK.md

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **quickshare** (2112 symbols, 5008 relationships, 156 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

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
