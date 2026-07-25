# QuickShareCloud — Agent Instructions

## Code Style

- **One statement per line** — do not chain multiple statements on a single line. Each statement must be on its own line.
- **No magic strings** — never use hardcoded string/number literals directly. Always use `const` or `enum` (strongly typed).
- **Isolation CSS** — always use scoped CSS (`.razor.css`) per component. JS goes into `wwwroot/js/` (do not use `.razor.js`).

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
