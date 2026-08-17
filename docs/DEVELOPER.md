# Developer Onboarding — QuickShare

Start here. Read top-to-bottom once (~15 min), then follow the pointers. This is the map;
`AGENTS.md` is the rulebook (read it second), the docs below are the detail.

## 1. Product (read first, 5 min)

- [`PRODUCT.md`](../PRODUCT.md) — what we build, users, principles ("Nhanh là tính năng",
  zero-knowledge verifiable, recipient-not-blocked).
- [`DESIGN.md`](../DESIGN.md) — the visual system. Source of record for anything visible.
  Read before touching CSS.

## 2. Codebase map

```
src/QuickShare/                 # monolith (FastEndpoints API + Blazor Static SSR)
  Features/<Feature>/           # vertical slice: endpoint + handler + DTOs in one folder
    Uploads/  Deliveries/  Downloads/  Shares/  Payments/  Users/  Analytics/
    Notifications/  Ads/  Admin/  ClientErrors/  Reports/
  Data/                         # AppDbContext, Entities/, Migrations/, AuditableEntityInterceptor
  Infrastructure/               # Storage/, Email/, Jobs/, FeatureFlags/, Telemetry/,
                                # Caching/, Middleware/, Constants/
  Components/                   # Pages/, Layout/, Shared/ (Blazor)
  ts/                           # TypeScript: download.ts, upload.ts, p2p/, ad-component.ts
  wwwroot/                      # css/ (app.css), js/, lib/
  Program.cs                    # DI + pipeline + endpoint registration
  App.razor / Routes.razor / _Imports.razor
```

One operation = one folder under `Features/`. Open the folder, see the whole flow
(endpoint → handler → DTO → mapper → tests).

## 3. Architecture (10 min)

- [`architecture.md`](architecture.md) — the spine: FastEndpoints + Blazor Static SSR +
  Mediator (martinothamar/Mediator) + EF Core/Npgsql + Htmxor + DAUB. Monolith; SSR pages call
  `IMediator` or `AppDbContext` in-process (no API round-trip).
- Feature flows: [`send-architecture.md`](send-architecture.md),
  [`payment-architecture.md`](payment-architecture.md),
  [`p2p-architecture.md`](p2p-architecture.md),
  [`login-architecture.md`](login-architecture.md),
  [`analytics-architecture.md`](analytics-architecture.md),
  [`profile-architecture.md`](profile-architecture.md).
- Ops: [`observability-setup.md`](observability-setup.md),
  [`feature-flags.md`](feature-flags.md),
  [`deployment-dev.md`](deployment-dev.md),
  [`backup-restore-runbook.md`](backup-restore-runbook.md).

## 4. Run it

```bash
dotnet run --project src/QuickShare                     # localhost:9000 (dev auto-signin on)
dotnet test tests/QuickShare.Server.Tests               # ~23s, 125 tests
dotnet test tests/QuickShare.Browser.Tests              # ~31s, 15 tests
# browser tests need chromium once: tests/.../playwright.ps1 install chromium
```

## 5. Conventions — non-negotiable

Full detail: [`csharp-conventions.md`](csharp-conventions.md) + `AGENTS.md`. The load-bearing
rules:

| Rule | Where |
|---|---|
| Vertical-slice folders; thin endpoint → `IMediator` → handler | csharp-conventions §Architecture |
| LightResults `Result`/`Result<T>` (no null/bool/exception) | csharp-conventions #6 |
| Mapperly `[Mapper]` for entity→DTO | csharp-conventions #8 |
| Audit fields auto-set by `AuditableEntityInterceptor` — never set manually | AGENTS.md |
| FastEndpoints auth-by-default — never gate on `IsAuthenticated` | AGENTS.md |
| FusionCache on read-heavy paths + invalidate after mutation | AGENTS.md |
| HTMX lazy-load default + `hx-select` on every `hx-get`/`hx-post` | AGENTS.md |
| `string.Empty` not `""`; no magic strings (centralize in `static class` consts) | AGENTS.md |
| User-facing copy in `SharedLoc.{en,vi}.resx` (never hardcoded) | AGENTS.md |
| One statement per line | AGENTS.md |

## 6. Workflow (issue → done)

1. Issue MUST carry a `## Manual Test Checklist` (action → expected result). No checklist →
   add it first. It drives the work and is the definition of done.
2. Before editing any symbol, run impact analysis (`gitnexus_impact`) — report blast radius.
3. Implement → run the automated suite for the touched area (see `docs/TESTING.md` regression
   matrix) → fill `## Manual Test Evidence` in the PR (check off each checklist step).
4. Update docs (AGENTS.md → Issue Completion Docs Update Rule): feature/architecture doc +
   specs/plans + `DESIGN.md` if UI changed. Docs land in the same commit series.
5. `gitnexus_detect_changes()` before commit — confirm only expected symbols changed.

## 7. Gotchas (bite every new dev)

- **No `AddHtmxorComponentEndpoints`** — throws `NullReferenceException` on .NET 10. HTMX
  responses come back as full HTML documents, so **every `hx-get`/`hx-post` needs `hx-select`**
  to extract the target element.
- **`MainLayout` inherits `LayoutComponentBase`** (not `HtmxLayoutComponentBase`) — else HTMX
  partials return full nav + footer.
- **Download page must work with no JavaScript** — never `class="hidden"` the default phase;
  `DownloadNoScriptTests` will fail.
- **Dev auto-signin** middleware lives in `Program.cs` — local auth is automatic.
- **Analytics are anonymous** — never store email/user-id/raw IP in events (`AnalyticsService`).
- **Security middleware** (order in `Program.cs`): `RateLimitingMiddleware` (per-IP/user, 429),
  `RobotsNoIndexMiddleware` (X-Robots-Tag on `/d/*` + `/api/downloads/*`, issue #121),
  `SecurityHeadersMiddleware` (config-driven CSP/HSTS; `SecurityHeaders:Enabled` kill switch for tests, issue #82).
- **OTP lockout** (#148): `OtpLockoutService` counts failures per delivery — 5 fails → 423 + Retry-After
  (15min → 1h → 24h escalating). **Turnstile** (#175): optional `Turnstile:SiteKey/SecretKey` config;
  unconfigured = challenge skipped. **Device alerts** (#149): `DeviceTrackerService` fingerprints
  UA+IP (SHA-256), alerts on first sighting; `/account/devices` manages known devices.
  **Abuse reports** (#53): anonymous POST `/api/deliveries/{code}/report` → admin `/admin/reports` (suspend/resolve).

## 8. Where the work is

- [`docs/backlog.md`](backlog.md) — feature inventory, tiered + status.
- [`docs/TESTING.md`](TESTING.md) — how a feature's acceptance is defined (test map + regression).
- `_bmad-output/specs/`, `docs/superpowers/specs/` — design specs per feature area.
- `docs/superpowers/plans/` — implementation plans.

---

## Keep fresh

Update this file when a new `Features/<X>/` folder, project, or convention is added.
See `AGENTS.md` → Issue Completion Docs Update Rule.
