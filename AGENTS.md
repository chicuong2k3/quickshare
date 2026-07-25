# QuickShareCloud — Agent Instructions

## Code Style

- **One statement per line** — do not chain multiple statements on a single line. Each statement must be on its own line.
- **No magic strings** — never use hardcoded string/number literals directly. Always use `const` or `enum` (strongly typed).
- **Isolation CSS/JS** — always use scoped CSS (`.razor.css`) and JS isolation (`.razor.js`) per component instead of dumping into global `wwwroot/`.

## Architecture Rules

### Audit Fields (CreatedAt / UpdatedAt)

- **Never set `CreatedAt` or `UpdatedAt` manually in code.** The `AuditableEntityInterceptor` (registered in `AppDbContext`) automatically sets:
  - `CreatedAt = DateTime.UtcNow` for all added entities that have a `CreatedAt` property.
  - `UpdatedAt = DateTime.UtcNow` for all added or modified entities that have an `UpdatedAt` property.
- Entities inheriting from `BaseAuditEntity` get both. Custom entities like `Manifest` with only `CreatedAt` get partial support automatically via metadata discovery.

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
