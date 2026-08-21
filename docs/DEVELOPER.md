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
    Notifications/  Ads/  Admin/  ClientErrors/  Reports/  FileRequests/
    Deliveries/LegalHold/       # #266: place/lift/CSV export; every deletion path filters the hold
    Users/Retention/            # #161: PUT policy + dry-run preview
    Downloads/Shared/           # DeliveryAccessService.cs, the #142/#166 scope gate that every
                                # recipient-facing content path routes through
  Data/                         # AppDbContext, Entities/, Migrations/, AuditableEntityInterceptor
  Infrastructure/               # Storage/, Email/, Jobs/, FeatureFlags/, Telemetry/,
                                # Caching/, Middleware/, Constants/
                                # RetentionPolicy.cs (#161), DeliveryAccessPolicy.cs (#142/#166):
                                # dependency-free static classes, so they unit-test without the
                                # web factory - that is why the rules live here, not in a handler
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
  [`billing-ledger.md`](billing-ledger.md) - team seats (#278), Studio overage (#141),
  e-invoice (#112), PO + net terms (#192),
  [`enterprise-trust.md`](enterprise-trust.md) - tenant isolation (#190), SIEM streaming (#191),
  self-serve DPA (#184), SOC 2 readiness (#147/#323),
  [`object-storage-and-backup.md`](object-storage-and-backup.md) - S3-compatible object store
  (#171), data residency (#119), scheduled workspace backup (#271),
  [`workspace-branding.md`](workspace-branding.md) - branded recipient page (#285),
  custom typography (#286),
  [`delivery-gating.md`](delivery-gating.md) - approval workflow (#162),
  recurring delivery (#176),
  [`comments-and-channels.md`](comments-and-channels.md) - delivery comments (#289),
  Slack/Teams posting (#180),
  [`p2p-architecture.md`](p2p-architecture.md),
  [`login-architecture.md`](login-architecture.md),
  [`analytics-architecture.md`](analytics-architecture.md),
  [`profile-architecture.md`](profile-architecture.md),
  [`compliance-architecture.md`](compliance-architecture.md) - legal hold (#266), workspace
  retention policy (#161), delivery scope rules (#142/#166),
  [`workspaces-architecture.md`](workspaces-architecture.md) - team workspaces (#106),
  ownership transfer (#252), workspace audit log (#145), admin usage report (#300),
  shared folders (#151), per-folder defaults (#245), folder templates (#234).
- Ops: [`observability-setup.md`](observability-setup.md),
  [`feature-flags.md`](feature-flags.md),
  [`deployment-dev.md`](deployment-dev.md),
  [`backup-restore-runbook.md`](backup-restore-runbook.md).

## 4. Run it

```bash
dotnet run --project src/QuickShare                     # localhost:9000 — no dev auto-signin;
                                                        # /account/* needs real Google OAuth creds
dotnet test tests/QuickShare.Server.Tests               # 561 tests
dotnet test tests/QuickShare.Browser.Tests              # 31 tests
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
| New code deleting delivery rows or R2 objects MUST filter `LegalHoldAt == null` (held bytes are evidence) | compliance-architecture.md (#266) |
| New code computing a delivery expiry MUST pass it through `RetentionPolicy.Clamp` (the policy is a ceiling) | compliance-architecture.md (#161) |
| New recipient-facing content path MUST route through `DeliveryAccessService` (scope rules are fail-closed) | compliance-architecture.md (#142/#166) |
| New code writing `User.Plan` MUST re-push it to workspace seats (a seat borrows the owner's plan) | workspaces-architecture.md (#106) |
| New upload path MUST use `QuotaService` (it pools a member's bytes onto the workspace owner) | workspaces-architecture.md (#106) |
| New upload path that can target a folder MUST enforce locked folder defaults | workspaces-architecture.md (#245) |
| Folder creation goes through `FolderWriter.CreateAsync`; deleting a folder never deletes content | workspaces-architecture.md (#151) |
| Every charge goes through `InvoiceService.AddOrUpdateLineAsync` (never a direct `InvoiceLine` insert) | billing-ledger.md (#278/#141) |
| `IsMetered` is allowed-and-billable, not blocked; never mutate an issued invoice | billing-ledger.md (#141/#112) |
| Every workspace-scoped route taking an id needs a `TenantIsolationTests` case (explicit `Where`, never a global query filter) | enterprise-trust.md (#190) |
| A cross-tenant miss returns the same response as a nonexistent id; never edit published DPA terms or a `/soc2` status in place | enterprise-trust.md (#190/#184/#147) |
| Object store config lives under `ObjectStore`; the legacy `R2` section must keep binding | object-storage-and-backup.md (#171) |
| Backup manifests carry metadata + storage keys, never bytes; prune only after a successful upload | object-storage-and-backup.md (#271) |
| Upload keys go through `R2ObjectKey.For` with the region; a key with no region segment must resolve to the default bucket | object-storage-and-backup.md (#119) |
| Never move stored bytes across regions as a side effect of a settings change or a file replace | object-storage-and-backup.md (#119) |
| A brand overrides token values only — never token names, never a type size | workspace-branding.md (#285/#286) |
| Brand CSS inputs are validated at the write boundary; uploaded assets are sniffed by magic bytes and SVG is refused | workspace-branding.md (#285/#286) |
| Any path publishing a delivery checks `PendingApproval`; it must be indistinguishable from not-found to recipients | delivery-gating.md (#162) |
| Any code copying a delivery carries `StorageRegion` and drops legal hold, approval state and `DeletedAt` | delivery-gating.md (#162/#176/#119) |
| Comment surfaces resolve all three gates via `CommentGate`; a chat webhook URL is never returned or audited | comments-and-channels.md (#289/#180) |
| Test stubs produce production shapes — a stub that invents a value hides whatever reads it | comments-and-channels.md |

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
