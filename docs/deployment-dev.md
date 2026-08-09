# Deployment Diagram — Dev (self-hosted runner)

CI/CD flow from GitHub Actions to a Windows Server, deploying the `quickshare`
container to Docker using a self-hosted runner.

## Overview

```mermaid
flowchart LR
    subgraph GitHub["GitHub"]
        REPO["chicuong2k3/quickshare<br/>branch: dev"]
        GHCR[("ghcr.io<br/>quickshare:dev")]
    end

    subgraph Server["Windows Server (this machine)"]
        RUNNER["Self-hosted Runner<br/>C:\\actions-runner"]
        ACTIONS["GitHub Actions<br/>(runs-on: self-hosted)"]
        COMPOSE["C:\\deploy\\quickshare<br/>docker-compose.yml + .env"]
        CONTAINER["Container: quickshare<br/>:8080"]
        CF["cloudflared tunnel<br/>→ quickshare.codemagic.io.vn"]
        DB[("PostgreSQL / pgBouncer<br/>(or third-party DB)")]
        VOLUME["Volume: quickshare_keys<br/>(DataProtection keys)"]
    end

    USER["Users<br/>browser"]

    REPO -->|"push branch dev"| ACTIONS
    ACTIONS -->|"build & push image"| GHCR
    ACTIONS -->|"build on runner + compose up -d"| COMPOSE
    COMPOSE --> CONTAINER
    CONTAINER -->|":8080"| CF
    CF --> USER
    CONTAINER -->|"ConnectionStrings__DefaultConnection"| DB
    CONTAINER --> VOLUME
```

## CI/CD flow

```mermaid
sequenceDiagram
    participant Dev as Developer
    participant GitHub as GitHub (dev branch)
    participant Runner as Self-hosted Runner (Windows)
    participant GHCR as ghcr.io
    participant Compose as docker compose (C:\deploy\quickshare)
    participant App as Container quickshare

    Dev->>GitHub: push to dev branch
    GitHub->>Runner: trigger "Deploy to Dev" workflow (runs-on: self-hosted)
    Runner->>Runner: checkout source
    Runner->>Runner: setup-buildx + login ghcr (GITHUB_TOKEN)
    Runner->>GHCR: build & push quickshare:dev + quickshare:<sha>
    Runner->>Runner: deploy job (needs: build)
    Runner->>Runner: docker build -t ghcr.io/chicuong2k3/quickshare:dev . (in workspace)
    Runner->>Runner: cd ${{ secrets.DEV_DEPLOY_PATH }}
    Runner->>Compose: docker compose up -d --remove-orphans (no pull — local tag)
    Compose->>App: recreate container when digest changes
    App->>App: migrate DB + create root folder "/"
    App-->>Runner: healthcheck HTTP :8080
```

## Components

| Component | Location / Value | Notes |
|-----------|------------------|-------|
| Workflow | `.github/workflows/deploy-dev.yml` | Triggers on push to `dev` branch or `workflow_dispatch`. Deploy job builds the image from the checked-out source (`docker build -t ghcr.io/chicuong2k3/quickshare:dev $GITHUB_WORKSPACE`) then `docker compose up -d`. |
| Image | `ghcr.io/chicuong2k3/quickshare:dev` | Lowercase (ghcr requirement); also tagged `:<commit-sha>` |
| Pull restriction | `GITHUB_TOKEN` cannot **pull** user-account (`ghcr.io/user/...`) container packages — write scope allows push only, pull returns `denied: denied`. That's why the deploy job never pulls; it builds locally on the runner instead. To pull on the server manually, `docker login ghcr.io` with a PAT (`read:packages`). |
| Runner | `C:\actions-runner`, repo `chicuong2k3/quickshare` | Registered to `Default` pool; both `build` and `deploy` jobs run on `self-hosted` |
| Deploy path | `C:\deploy\quickshare` | Secret `DEV_DEPLOY_PATH` points to this folder |
| Compose | `C:\deploy\quickshare\docker-compose.yml` | Service `quickshare`, binds `127.0.0.1:8080` |
| Env vars | `C:\deploy\quickshare\.env` | Read by compose, passed into the container as `__`-separated keys |
| Container port | `8080` (internal) | `ASPNETCORE_URLS=http://+:8080`, non-root `appuser` |
| Network | `cfnet` (external, shared with cloudflared) | Tunnel routes `http://quickshare:8080` |
| Key volume | `quickshare_keys:/app/keys` | DataProtection keys, keeps auth cookies stable |
| Public URL | `https://quickshare.codemagic.io.vn` | Cloudflare tunnel (host) → `http://quickshare:8080` (cfnet network) |
| Database | `DB_CONNECTION_STRING` in `.env` | PostgreSQL (host or third-party) |

## Env var → app config flow

```
.env on the server
   │  (docker compose reads ${VAR})
   ▼
docker-compose.yml  environment:
   ConnectionStrings__DefaultConnection=${DB_CONNECTION_STRING}
   Authentication__Google__ClientId=${GOOGLE_CLIENT_ID}
   R2__AccountId=${R2_ACCOUNT_ID}
   PayOS__ClientId=${PAYOS_CLIENT_ID}
   │
   ▼
Container env var  →  IConfiguration (appsettings.json + env var override)
```

## Manual deploy (test)

```powershell
cd C:\deploy\quickshare
# Build from the runner workspace (checkout of the dev branch):
docker build -t ghcr.io/chicuong2k3/quickshare:dev <runner workspace path>
docker compose up -d --remove-orphans quickshare
docker compose logs -f quickshare
```

> If you must pull from ghcr.io instead (PAT required, `read:packages`):
> `echo $PAT | docker login ghcr.io -u chicuong2k3 --password-stdin` then `docker compose pull quickshare`.

## Verify workflow run

- GitHub Actions tab → latest run → `build` + `deploy` jobs
- On the server, a running job spawns `Runner.Worker`; logs at `C:\actions-runner\_diag\Worker_*.log`
- Runner status: repo → Settings → Actions → Runners

## Notes

- Missing `DB_CONNECTION_STRING` → app crashes at startup.
- Wrong Google OAuth → login does not work (other OAuth providers are optional).
- R2 not configured → app falls back to local disk; uploads via R2 will fail.
- Cloudflare tunnel runs in the `cfnet` network, routes `http://quickshare:8080` (quickshare is on the same `cfnet` network, so no host port is exposed publicly; `127.0.0.1:8080` binding is only for local host access).

### P2P

- P2P is ICE-direct only (STUN, no TURN). Cross-network transfers (two different 4G carriers, strict NAT on both sides) will always fail with the "Direct connection failed" message — that failure is the signal to add a TURN server (cost+bandwidth tradeoff; the R2 server path already exists as the fallback).
- Rewarded-ad unlock (`Ads:DevSimulation=true` in the app `.env`) renders a "Simulate rewarded ad" button on the ad card so the daily-limit/unlock flow can be tested without an AdMob/GAM setup. Server-side SSV is a placeholder that accepts any non-empty token, so the dev button must NEVER be exposed in production (the real Google SSV check is TODO).
