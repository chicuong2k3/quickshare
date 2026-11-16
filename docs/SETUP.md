# Environment Setup — QuickShare

From a blank machine to a running app + deploy. For DevOps onboarding, CI/CD, and local dev.
Read top-to-bottom; skip optional sections if you only need local dev.

## 1. Prerequisites (install once)

| Tool | Version | Why |
|---|---|---|
| .NET SDK | **10.0.x** (`net10.0`) | build + run |
| Node.js + npm | **20** | TypeScript build (`tsc` → `wwwroot/js`), auto-run on `dotnet build` |
| PostgreSQL | 16+ | required DB — app crashes at startup without a connection string |
| Docker + Docker Compose | latest | deploy + observability stack |
| PowerShell | 7+ (`pwsh`) | CI Playwright install step |
| Git | latest | clone |

Optional, only if you enable the feature:

| Tool | Feature |
|---|---|
| Playwright Chromium | browser tests (`playwright.ps1 install chromium`) |
| Cloudflare account + R2 bucket | object storage (fallback = local disk) |
| PayOS account | VN payments |
| Google Cloud OAuth client | Google login |
| Brevo (SMTP) account | email notifications |
| `cloudflared` | public URL via Cloudflare tunnel |
| Slnmap 0.13.1 (.NET global tool) | optional semantic impact analysis over `QuickShare.slnx` via MCP |

## 2. One-time setup

```powershell
git clone https://github.com/chicuong2k3/quickshare.git
cd quickshare
dotnet restore
dotnet tool restore          # installs dotnet-ef 10.0.10 (dotnet-tools.json)
# npm deps auto-install on first dotnet build (BuildTypeScript MSBuild target)
```

For semantic impact analysis (optional): install `Slnmap` as a global .NET tool, run `slnmap analyze QuickShare.slnx --db slnmap.db` from the repository root, then point the `slnmap` MCP entry's `--db` argument to the **absolute** path of the resulting database. The generated `slnmap.db*` files are ignored by Git. Restart the MCP client after changing its configuration. Re-run analysis after modifying C# code; Razor markup is not indexed by Slnmap.

```powershell
dotnet tool install --global Slnmap
slnmap analyze QuickShare.slnx --db slnmap.db
slnmap status --db slnmap.db
```

## 3. Minimal local run (dev)

```powershell
# 1. PostgreSQL running + connection string. Local dev uses appsettings.Development.json
#    or user-secrets (UserSecretsId: f14da91e-ee6c-4a2e-8f55-0ac6329eed3c):
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=quickshare;Username=quickshare;Password=..." --project src/QuickShare

# 2. Run (dev auto-signin middleware in Program.cs — no OAuth needed locally):
dotnet run --project src/QuickShare
# → http://localhost:9000
```

Local dev does **not** read `.env` (that file is for Docker Compose on the server). Config
order: `appsettings.json` → `appsettings.Development.json` → user-secrets → env vars.

## 4. Optional services (enable per feature)

All optional except PostgreSQL. Set via env vars (Docker) or appsettings/user-secrets (local).

| Feature | Config section | Env var (server `.env`) | Blank = |
|---|---|---|---|
| Google login | `Authentication:Google` | `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | login broken |
| GitHub login | `Authentication:GitHub` | `GITHUB_CLIENT_ID`, `GITHUB_CLIENT_SECRET` | disabled |
| R2 storage | `R2:*` | `R2_ACCOUNT_ID`, `R2_BUCKET_NAME`, `R2_ACCESS_KEY_ID`, `R2_ACCESS_KEY_SECRET` | local disk fallback (R2 uploads fail) |
| PayOS | `PayOS:*` | `PAYOS_CLIENT_ID`, `PAYOS_API_KEY`, `PAYOS_CHECKSUM_KEY`, `PAYOS_WEBHOOK_URL` | payments disabled |
| Email | `Email:*` | `EMAIL_HOST`, `EMAIL_PORT`, `EMAIL_USER`, `EMAIL_PASSWORD`, `EMAIL_FROM`, `EMAIL_FROM_NAME` | email notify off |
| Pricing | `Pricing:*` | `PRICING_PRO_MONTHLY`, … (defaults in docker-compose) | defaults |
| Admin flags | `Admin:Emails` | `ADMIN_EMAILS` (comma-separated) | no admin access |
| Observability | `Observability:OtlpEndpoint` | `OBSERVABILITY_OTLP_ENDPOINT` | localhost:4318 |
| Release tracking | — | `GIT_SHA` | "dev" |

Full annotated list: [`.env.example`](../.env.example).

## 5. Tests

```powershell
dotnet test tests/QuickShare.Server.Tests    # ~23s, 125 tests (in-process, SQLite per-factory)
# browser tests need chromium once:
pwsh tests/QuickShare.Browser.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test tests/QuickShare.Browser.Tests   # ~31s, 15 tests (drives installed Chrome)

dotnet format --verify-no-changes            # CI enforces formatting
```

Build is strict: `TreatWarningsAsErrors=true` (see `Directory.Build.props`).

## 6. Docker deploy (Coolify)

Production is deployed by **Coolify** (`https://coolify.codemagic.io.vn`) on the Coolify host machine,
which builds this repo's `Dockerfile` from `master`. The container listens on `8080` as non-root
`appuser`; env vars live on the Coolify app, not in a file in this repo.

```bash
API=https://coolify.codemagic.io.vn/api/v1
APP=l4wrthznhxtp5ghfzazgvgcs
curl -X POST -H "Authorization: Bearer $COOLIFY_TOKEN" "$API/deploy?uuid=$APP"
curl -s -H "Authorization: Bearer $COOLIFY_TOKEN" "$API/applications/$APP/logs"
```

- Public URL `https://quickshare.codemagic.io.vn` = Cloudflare wildcard CNAME → `cloudflared`
  tunnel → Traefik on the Coolify host → container `:8080`. No explicit DNS record needed.
- The app must run on the **Coolify host machine**, otherwise that Traefik has no router for the
  hostname and answers `404 page not found`.
- DataProtection keys sit at `/app/keys` in the container filesystem (not a persistent volume), so a
  rebuild logs everyone out.

Full topology, resource IDs and rollback: [`deployment.md`](deployment.md).

## 7. CI/CD (GitHub Actions)

| Job | Runner | Trigger | Does |
|---|---|---|---|
| `test` | ubuntu | push/PR `master`/`dev` | restore → build → format → server tests → Playwright chromium → browser tests |
| `release` | ubuntu | push `master` | semver tag + GitHub release |
| `backup` | self-hosted | daily 02:00 UTC | `ops/backup` image → `age`-encrypted `pg_dump` → versioned R2 bucket |
| `restore-drill` | self-hosted | quarterly | restores the latest backup into a scratch DB |

Deployment itself is **not** a GitHub Actions job — the Coolify GitHub webhook deploys on push to
`master`. Backups and restore drill: [`backup-restore-runbook.md`](backup-restore-runbook.md)
(note: the runner's `BACKUP_DB_CONNECTION_STRING` still points at the pre-Coolify database host).

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Startup crash | `DB_CONNECTION_STRING` missing/empty | set Postgres connection string |
| Login broken | Google OAuth client id/secret missing | fill `Authentication:Google:*` |
| Uploads fail | R2 not configured | fill `R2:*` (or accept local-disk fallback, but R2 path fails) |
| `Could not load ... EFCore.Relational 10.0.10.0` | Relational float mismatch | already pinned in csproj — don't unpin |
| Browser tests fail to launch | Chromium not installed | `playwright.ps1 install chromium` |
| `dotnet build` slow first time | `npm ci` runs | expected once; cached after |
| `hx-*` partial returns full HTML | missing `hx-select` | every `hx-get`/`hx-post` needs `hx-select` (no Direct Routing on .NET 10) |

## 9. Keep fresh

Update this file when a new env var, service dependency, tool version, or setup step is added.
See AGENTS.md → Issue Completion Docs Update Rule.
