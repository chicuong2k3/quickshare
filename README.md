# QuickShareCloud

**Send files, not friction.** Any-size file sharing over a link — chunked upload, end-to-end
AES-256-GCM encryption (key never leaves the browser URL fragment), SHA-256-verified download.
Recipients open the link and download; no account, no app.

## Stack

.NET 10 · FastEndpoints · Blazor Static SSR + Htmxor · Wolverine (inbox/outbox) · EF Core/Npgsql
· PostgreSQL · Cloudflare R2 · PayOS (VN payments) · TypeScript · DAUB design system

## Quick start

```bash
# needs .NET 10 SDK + PostgreSQL (connection string via user-secrets)
dotnet run --project src/QuickShare     # → http://localhost:9000
```

Full setup (env vars, optional services, Docker deploy, CI/CD):
[`docs/SETUP.md`](docs/SETUP.md).

## Tests

```bash
dotnet test tests/QuickShare.Server.Tests    # 125 in-process tests (~23s)
dotnet test tests/QuickShare.Browser.Tests   # 15 Playwright tests (~31s, needs chromium)
```

## Onboarding — pick your role

| Role | Read first |
|---|---|
| Developer | [`docs/DEVELOPER.md`](docs/DEVELOPER.md) |
| Tester / QA | [`docs/TESTING.md`](docs/TESTING.md) |
| DevOps | [`docs/SETUP.md`](docs/SETUP.md) |
| Support / on-call | [`docs/ONCALL.md`](docs/ONCALL.md) |
| Product / design | [`PRODUCT.md`](PRODUCT.md) + [`DESIGN.md`](DESIGN.md) |
| Feature inventory (all roles) | [`docs/backlog.md`](docs/backlog.md) |

## Docs index

- `PRODUCT.md` — what + who + principles
- `DESIGN.md` — visual system (source of record for UI)
- `docs/backlog.md` — feature inventory, tiered + status
- `docs/DEVELOPER.md` — codebase map, conventions, workflow
- `docs/TESTING.md` — test map, regression matrix
- `docs/SETUP.md` — prerequisites, env vars, deploy
- `docs/ONCALL.md` — incident response + rollback playbooks
- `docs/architecture.md` — system spine
- `docs/send-architecture.md`, `docs/payment-architecture.md`, `docs/p2p-architecture.md`,
  `docs/login-architecture.md`, `docs/analytics-architecture.md` — feature flows
- `docs/observability-setup.md`, `docs/backup-restore-runbook.md`, `docs/deployment-dev.md` — ops
- `AGENTS.md` — AI agent rules (conventions + workflow; not human onboarding)
