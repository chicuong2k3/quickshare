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
    GHCR -->|"docker pull"| RUNNER
    RUNNER -->|"docker compose up -d"| COMPOSE
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
    Runner->>Runner: docker login ghcr.io
    Runner->>Runner: cd ${{ secrets.DEV_DEPLOY_PATH }}
    Runner->>Compose: docker compose pull quickshare
    Compose->>GHCR: pull quickshare:dev
    Compose->>App: docker compose up -d quickshare
    App->>App: migrate DB + create root folder "/"
    App-->>Runner: healthcheck HTTP :8080
```

## Components

| Component | Location / Value | Notes |
|-----------|------------------|-------|
| Workflow | `.github/workflows/deploy-dev.yml` | Triggers on push to `dev` branch or `workflow_dispatch` |
| Image | `ghcr.io/chicuong2k3/quickshare:dev` | Lowercase (ghcr requirement); also tagged `:<commit-sha>` |
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
docker compose pull quickshare
docker compose up -d --remove-orphans quickshare
docker compose logs -f quickshare
```

## Verify workflow run

- GitHub Actions tab → latest run → `build` + `deploy` jobs
- On the server, a running job spawns `Runner.Worker`; logs at `C:\actions-runner\_diag\Worker_*.log`
- Runner status: repo → Settings → Actions → Runners

## Notes

- Missing `DB_CONNECTION_STRING` → app crashes at startup.
- Wrong Google OAuth → login does not work (other OAuth providers are optional).
- R2 not configured → app falls back to local disk; uploads via R2 will fail.
- Cloudflare tunnel runs in the `cfnet` network, routes `http://quickshare:8080` (quickshare is on the same `cfnet` network, so no host port is exposed publicly; `127.0.0.1:8080` binding is only for local host access).
