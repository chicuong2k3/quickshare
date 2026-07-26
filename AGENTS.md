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
     hx-swap="outerHTML">
    @* skeleton markup *@
</div>
```

```html
@* All navigation links use HTMX *@
<a href="/page?filter=X"
   hx-get="/page?filter=X"
   hx-target="#page-content"
   hx-swap="outerHTML"
   hx-push-url="true">Filter X</a>
```

### Exceptions

- **Truly static pages** with zero async operations (pure markup, no DB/API calls) do not need lazy-load.
- `[StreamRendering]` is acceptable only when splitting shell + partial is impractical (complex interdependencies). Even then, prefer lazy-load.

### Layout Rule

- **Do NOT put `@layout MainLayout` on individual pages.** It is set globally in `_Imports.razor`. Explicit `@layout` on a page overrides `[HtmxLayout]`, causing HTMX partial requests to return the full page (nav + footer) instead of just the content area.

## Code Style

- **One statement per line** — do not chain multiple statements on a single line. Each statement must be on its own line.
- **No magic strings** — never use hardcoded string/number literals directly. Always use `const` or `enum` (strongly typed).
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
