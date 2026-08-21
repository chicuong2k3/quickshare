# QuickShare — Architecture

## Projects

| Project | Role | Port | Framework |
|---------|------|------|-----------|
| `QuickShare` | Monolith: FastEndpoints API + Blazor Static SSR | `9000` (local dev) / `8080` (Docker) | .NET 10, FastEndpoints, Mediator, Hangfire, EF Core, Npgsql, Htmxor, DAUB |
| `QuickShare.Querying` | Dynamic sort/paging engine + source generators | — | .NET 10 |
| `QuickShare.Server.Tests` | Unit tests | — | xUnit |

> **July 2026:** `QuickShare.Server` (API backend, port 59155) and `QuickShare.Web` (Blazor frontend, port 9000) merged into a single `QuickShare` monolith. `QuickShare.Shared` models/constants also folded in. Removed Refit, `IQuickShareApi`, `CookieForwardingHandler` — SSR pages now call `IMediator` or `AppDbContext` in-process. JS API calls use relative URLs (same origin, no CORS).
>
> In Docker the app listens on `8080` (`ASPNETCORE_URLS=http://+:8080`) and runs as non-root `appuser`. See [Deployment — Dev (self-hosted runner)](deployment-dev.md).

---

## Deployment (Dev)

CI/CD deploys the app to a Windows server via a self-hosted GitHub Actions runner. Both `build` and `deploy` jobs run on `self-hosted` (no GitHub-hosted runner):

- **Image**: `ghcr.io/chicuong2k3/quickshare:dev` (lowercase — ghcr requirement)
- **Runner**: `C:\actions-runner`, repo `chicuong2k3/quickshare`, pool `Default`
- **Deploy path**: `C:\deploy\quickshare` (`docker-compose.yml` + `.env`), service `quickshare`, binds `127.0.0.1:8080`
- **Public URL**: `https://quickshare.codemagic.io.vn` (Cloudflare tunnel on host → `http://localhost:8080`)
- **Env vars**: `.env` on server → compose `environment:` → container `IConfiguration` (keys like `ConnectionStrings__DefaultConnection`, `Authentication__Google__ClientId`, `R2__AccountId`, `PayOS__ClientId`)
- **Key volume**: `quickshare_keys:/app/keys` — DataProtection keys, keeps auth cookies stable

Full diagrams + flow: [docs/deployment-dev.md](deployment-dev.md).

---

## Server Features

### Auth
- **Cookie auth** with dual behavior: `/api/*` paths return 401 (API behavior), other paths redirect `/login` (page behavior)
- **Google OAuth** — `OnCreatingTicket` calls `UserRegistrationService.RegisterAsync()` in-process (shared across all OAuth providers)
- `ClaimsPrincipalExtensions.cs` — User ID extraction via `GetUserAsync(db, ct)`
- **Dev auto-signin** middleware — creates `dev-user` claims for local development

### Uploads (`Features/Uploads/`)
- **R2 Multipart** (`StartR2`, `CommitR2`, `AbortR2`) — Cloudflare R2 presigned multipart upload flow
- `UploadConstants.cs` — Upload limits and magic numbers

### Downloads (`Features/Downloads/`)
- **Info** (`DownloadInfoHandler`) — File metadata with download gates:
  - OTP gate (if `HasOtp`) — receiver must enter the 6-digit code set by sender
  - Password gate (if `HasPassword`)
  - Ad interstitial (free plan)
  - Download limit enforcement
  - Called in-process via `IMediator` from `Download.razor` (no API endpoint)
- **Download** (`DownloadDeliveryEndpoint` + `Handler`) — Stream file from storage to client, supports AES-GCM client-side decryption. All uploads are now AES-256-GCM encrypted by default (no toggle).

### Files (`Features/Files/`)
- **List** — `ListFilesHandler` called via `IMediator` from `Files.razor` (in-process, no API endpoint)
- **Move** — `PATCH /api/files/{id}/folder` — drag-drop between folders (called from files.js)
- **Download** — Direct file download by ID
- `Shared/Errors.cs` — File-specific error types
- `Shared/FileMapper.cs` — Mapperly source-gen mapper (FileMetadata → FileItemResponse)

### Folders (`Features/Folders/`)
| Endpoint | Method | Path | Consumer |
|----------|--------|------|----------|
| Create | POST | `/api/folders` | files.js |
| Update | PUT | `/api/folders/{id}` | files.js |
| Delete | DELETE | `/api/folders/{id}` | files.js |
| List | — | (in-process) | `Files.razor` via `IMediator` → `ListFoldersHandler` |
| GetInfo | — | (in-process) | `Files.razor` via `IMediator` → `GetFolderInfoHandler` |

### Deliveries (`Features/Deliveries/`)
- **List** — `GET /api/deliveries` — user's delivery history
- **Brand** — `GET/PUT /api/deliveries/{id}/brand` — custom delivery branding (logo URL, message, accent color)
- **Password** — `POST /api/deliveries/{code}/verify` — verify password-protected delivery
- **OTP** — `POST /api/deliveries/{code}/verify-otp` — verify 6-digit OTP code set by sender on upload
- `DeliveryService.cs` — Shared delivery coordination logic
- **Compliance controls** — legal hold (#266), account retention policy (#161), delivery IP allowlist + country rules (#142/#166). See [docs/compliance-architecture.md](compliance-architecture.md)
- **Delivery gating** — workspace/folder approval before a link goes live (#162), recurring auto-send with a fresh link per cycle (#176). See [docs/delivery-gating.md](delivery-gating.md)
- **Workspace branding** — logo, accent colours, header/footer copy and self-hosted woff2 typography on recipient-facing pages (#285/#286). See [docs/workspace-branding.md](workspace-branding.md)
- **Object storage & backup** — any S3-compatible endpoint via `ObjectStore` config, R2/MinIO/Garage/Wasabi/AWS (#171); per-delivery storage region with key-derived bucket routing (#119); scheduled workspace manifest export to customer-controlled buckets (#271). See [docs/object-storage-and-backup.md](object-storage-and-backup.md)
- **Enterprise trust** — tenant isolation + cross-tenant probe logging (#190), audit-log streaming to Splunk/Elastic/CEF (#191), self-serve DPA (#184), published SOC 2 control map (#147/#323). See [docs/enterprise-trust.md](enterprise-trust.md)
- **Billing ledger** — per-seat billing (#278), Studio overage metering (#141), VAT invoices (#112), purchase orders + net terms (#192). See [docs/billing-ledger.md](billing-ledger.md)
- **Team workspaces** — seats + pooled quota + shared history (#106), ownership transfer (#252), workspace audit log (#145), admin usage report (#300), shared folders + per-folder upload defaults + folder templates (#151/#245/#234). See [docs/workspaces-architecture.md](workspaces-architecture.md)

### Payments (`Features/Payments/`)
- **PayOS Webhook** — `POST /api/payments/payos/webhook` — PayOS payment webhook. PayOS is a **payment gateway** supporting bank transfer (VietQR), card, and e-wallet. Subscription management (cancel, change, expiry) is handled in-app.
  - Records payments as `PaymentEvent`
  - For subscription payments, delegates to `SubscriptionService` for in-app lifecycle management
  - Matches user by **`PayOSOrderCode`** (numeric) — looks up `PaymentIntent` by `orderCode` → finds user
- **Upgrade Initiation** — `Upgrade.razor` `<form method="post">` handler creates a `PaymentIntent` with `PayOSOrderCode`, expected amount, and 1-hour expiry. Calls `PayOSService.CreatePaymentLinkAsync()` to generate PayOS checkout URL + QR. In-process, no API endpoint.
- **PayOSService** — `Features/Payments/PayOS/PayOSService.cs` — creates PayOS payment links via `POST /v2/payment-requests` API. Manages `PayOSClient` initialization.
- **Subscriptions** — In-app subscription lifecycle via `SubscriptionService`:
  - Cancel: `AccountBilling.razor` `<form method="post">` handler calls `SubscriptionService.CancelAsync()` directly
  - Current: `AccountBilling.razor` calls `SubscriptionService.GetCurrentAsync()` directly in `OnInitializedAsync`
  - Billing history: `AccountBilling.razor` calls `BillingHistoryService.GetForUserAsync()` directly
  - `SubscriptionExpiryJob` — Background service (runs every 15min) expires stale subscriptions and downgrades `User.Plan`
  - `Subscription` entity tracks: Plan, Status (Active/Cancelled/Expired), BillingPeriod, CurrentPeriodStart/End

### Reports (`Features/Reports/`)
- `ReportDeliveryEndpoint` + `Handler` — `POST /api/deliveries/{code}/report` — abuse reporting (spam, malware, abuse, other)

### Shares (`Features/Shares/`)
- **P2P** — `POST /api/shares/p2p` — create P2P share code + WebRTC signaling metadata

### Analytics (`Features/Analytics/`)
- Anonymous product analytics (issue #24) — no PII, no user ids, no raw IPs
- Events recorded at upload start/commit, download, P2P create/join, OTP pass/fail, ad interstitial, signup
- Client beacon `POST /api/analytics/track` (allowlisted events, sets `qs_visitor` cookie)
- Founder dashboard `/admin/analytics` + `GET /api/admin/analytics` — DAU, funnel, top files, geo spread
- See [analytics-architecture.md](analytics-architecture.md)

### Users (`Features/Users/`)
- **Profile** — `Account.razor` `<form method="post">` handler reads name/avatar, re-signs cookie. In-process via `IMediator` or `AppDbContext`.
- **Registration** — `UserRegistrationService.RegisterAsync()` called in-process from Google OAuth `OnCreatingTicket` (DRY — same service used by all providers)
- `Shared/UserRegistrationService.cs` — Registration logic shared between OAuth providers

---

## Infrastructure

| Component | Purpose |
|-----------|---------|
| **PostgreSQL** | Users, logins, files metadata, folders, deliveries, usage tracking. EF Core + Npgsql |
| **Cloudflare R2** (`R2StorageProvider.cs`) | S3-compatible file blob storage, presigned URLs, multipart upload |
| **Local Disk** (`LocalDiskStorageProvider.cs`) | Fallback/development disk storage |
| **Mediator** | In-process request/response dispatch (martinothamar/Mediator source-generated handlers, scoped lifetime) — replaces the former Wolverine message bus |
| **FastEndpoints** | REPR-pattern (Request-Endpoint-Response) minimal API framework |
| **FastEndpoints Swagger** | OpenAPI/Swagger document generation at `/swagger` |
| **QuotaEnforcer** | Per-plan limits: file size caps, monthly upload quota, transfer counts |
| **DataProtection** | `PersistKeysToFileSystem` with `SetApplicationName("QuickShare")` — single app, no cross-process key sharing needed |
| **Hangfire** | Background jobs: 7 recurring maintenance jobs with dashboard at `/hangfire` (admin-gated via `AdminDashboardAuthorizationFilter`). Postgres storage. Replaces the hand-rolled `BackgroundService` loops. |
| **Backup / Restore** | `ops/backup/*.sh` + `.github/workflows/backup.yml` (daily) + `restore-drill.yml` (quarterly). `pg_dump` → `age` encrypt → R2 versioned bucket, 30-day retention. See [backup-restore-runbook.md](backup-restore-runbook.md). |
| **TrialService** | 14-day free trial tracking |
| **GlobalExceptionHandler** | Centralized `ProblemDetails` error responses |
| **RequestLoggingMiddleware** | Structured inbound request logging |
| **P2P Signaling** | `P2PConnectionManager` (singleton) + `P2PSignalingMiddleware` — WebSocket-based WebRTC signaling relay |
| **Rate Limiting** | `RateLimitingMiddleware` — configurable request throttling |
| **HTTP Caching** | `HtmxCacheMiddleware` — `Cache-Control` headers for SSR/HTMX pages (2min private default, 5min public for home/pricing). Skips `/api/*`, `/send`, `/login`, `/logout`. |

---

## Blazor SSR Pages

| Route | Page | Interactivity | Key Features |
|-------|------|--------------|-------------|
| `/` | `Home.razor` | Server-rendered | Product landing page: hero, feature cards (encryption, auto-expiry, no signup), 3-step how-it-works, CTA |
| `/send` | `Upload.razor` | Vanilla JS | Drag-drop + paste upload, AES-256-GCM always-on encryption, R2 multipart, QR code generation, expiry selector (24h/7d), optional 6-digit OTP security code |
| `/d/{code}` | `Download.razor` | Vanilla JS | Multi-phase download: OTP gate (if set) → password gate → ad interstitial (free plan) → AES-GCM decrypt → file save. ZK attestation, abuse reporting, sender branding |
| `/p2p/send` | `P2PTransfer.razor` | Vanilla JS + WebRTC | P2P sender: create share code, WebRTC signaling, data channel file streaming with progress |
| `/p2p/d/{code}` | `P2PDownload.razor` | Vanilla JS + WebRTC | P2P receiver: signaling connect, WebRTC answer, data channel file receipt, progress tracking |
| `/files` | `Files.razor` | Vanilla JS + htmx | File browser: stats dashboard (4 cards), folder strip with drag-drop move, CRUD modals, htmx-powered live search, download button |
| `/account` | `Account.razor` | Vanilla JS + htmx | Profile edit (name, avatar upload), OAuth badge, delivery branding manager, htmx form + lazy-load |
| `/account/billing` | `AccountBilling.razor` | `<form method="post">` | Billing history, subscription status, cancel via form handler |
| `/upgrade` | `Upgrade.razor` | `<form method="post">` | Plan upgrade, PayOS payment — form creates PaymentIntent + calls PayOS API |
| `/login` | `Login.razor` | Server-side | OAuth provider buttons: Google, GitHub |
| `/pricing` | `Pricing.razor` | htmx | 3-tier plan cards (Free/Pro/Studio), monthly/yearly toggle, FAQ accordion, CTA |

### SSR Data Access Pattern

SSR pages that previously called the API via HTTP (`IQuickShareApi`/Refit) now access data **in-process**:

| Page | Old (HTTP) | New (in-process) |
|------|-----------|------------------|
| `Files.razor` | `Api.GetFoldersAsync()`, `Api.GetFilesAsync()` | Inject `IMediator`, call `ListFoldersQuery`, `ListFilesQuery` directly |
| `Account.razor` | `Api.GetMyProfileAsync()`, `Api.UpdateMyProfileAsync()` | `<form method="post">` handler updates profile via `AppDbContext`; data loaded via `IMediator` |
| `AccountBilling.razor` | `Api.GetBillingHistoryAsync()`, `Api.CancelSubscriptionAsync()` | Inject `BillingHistoryService`, `SubscriptionService` — call directly in `OnInitializedAsync` and form handler |
| `Upgrade.razor` | `Api.InitiateUpgradeAsync()`, `Api.GetMyProfileAsync()` | `<form method="post">` handler creates `PaymentIntent` via `AppDbContext` directly; profile loaded via `IMediator` |

**Rule:** If the endpoint's handler goes through `IMediator.Send<T>(cmd, ct)`, the page injects `IMediator` and calls the same command/query. If the endpoint uses `AppDbContext` inline, the page injects `AppDbContext` and queries directly with Mapperly mappers.

JS-based pages (Upload, Download, P2PTransfer, P2PDownload) call `/api/*` via `fetch()`/`XMLHttpRequest` — these use **relative URLs** (empty `apiBaseUrl` in `window.ZAPDROP_CONFIG`), so they work same-origin with no CORS.

### Component Layout
- **`MainLayout.razor`** — Flex-column layout: header (logo + nav) → `@Body` → footer. Inherits `HtmxLayoutComponentBase` for htmx partial rendering. Navbar: DAUB nav-menu + avatar dropdown (initials + email + Account/Sign out links).
- **`HtmxorLayout.razor`** — Base htmx layout component
- **`App.razor`** — Root HTML: DAUB anti-FOUC script, `daub.css` CDN, `hx-boost="true"` on body, `daub.js` CDN, theme init (grunge-dark default), QR lib

### Technology Stack

| Layer | Technology |
|-------|-----------|
| **Framework** | .NET 10 Blazor Static SSR with Htmxor |
| **UI** | DAUB (`daub.dev/daub.css` + `daub.js`) — grunge-dark theme, 84 components |
| **Interactivity** | htmx (`hx-boost`, `hx-get`, `hx-post`, `hx-target`, `hx-swap`) + vanilla JS modules |
| **Styling** | DAUB component classes + CSS isolation via `.razor.css` (Blazor auto-scope) |
| **CSS Variables** | `--color-foreground`, `--color-background`, `--color-primary`, `--color-accent`, `--color-border`, `--color-muted`, `--color-destructive`, `--color-warning`, `--color-success`, `--color-ring` |
| **P2P** | Custom `RTCPeerConnection` + data channels (`/wwwroot/js/p2p/`): `signaling.js`, `peerConnection.js`, `fileTransfer.js`, `fileReceiver.js` |
| **QR** | `qrcode-generator` (npm vendor-copied to `wwwroot/lib/qrcode/qrcode.min.js`) |
| **API** | JS pages call relative `/api/*` (same origin); SSR pages use in-process `IMediator` or `AppDbContext` |
| **Auth** | `AuthorizeRouteView` + `[Authorize]` on protected pages; cookie auth with dual `/api` vs page redirect behavior |
| **Anti-forgery** | `app.UseAntiforgery()` + `app.UseHtmxAntiforgery()` |

### Static Assets (`wwwroot/`)
```
wwwroot/
├── js/
│   ├── upload.js            # R2 multipart + AES-GCM encryption + QR renderer
│   ├── download.js          # Multi-phase download flow
│   ├── files.js             # File browser interactivity
│   ├── account.js           # Profile + avatar management
│   └── p2p/
│       ├── signaling.js     # WebSocket signaling client
│       ├── peerConnection.js # RTCPeerConnection wrapper
│       ├── fileTransfer.js  # FileSender (chunked data channel)
│       ├── fileReceiver.js  # FileReceiver (chunked data channel)
│       ├── send.js          # Sender page coordinator
│       ├── download.js      # Receiver page coordinator
│       └── encryption.js    # AES-GCM key derivation + encrypt/decrypt
├── lib/
│   └── qrcode/
│       └── qrcode.min.js    # QR code generator (from npm)
└── avatars/                 # User-uploaded avatar images
```

---

## Data Models & Constants (Entities + Infrastructure)

Entities live in `src/QuickShare/Data/Entities/` (namespace `QuickShare.Data.Entities`); constants live in `src/QuickShare/Infrastructure/`.

### Data Models

| Entity | Table | Key Fields |
|--------|-------|-----------|
| `User` | `users` | Id (Guid v7), Name, Email, AvatarUrl, Plan (Free/Pro/Studio), CreatedAt, UpdatedAt |
| `UserLogin` | `user_logins` | Id, UserId (FK), Provider (google — only Google OAuth is configured), ProviderKey |
| `UserUsage` | `user_usages` | Id, UserId (FK), PeriodStart (month), TotalBytesUploaded, TransferCount |
| `Folder` | `folders` | Id (UUID), Name (UNIQUE) |
| `FileMetadata` | `files` | Id (UUID), Filename, ContentType, Size, FolderId (FK), Tags (TEXT[]), CreatedAt |
| `Delivery` | `deliveries` | ShareCode, FileName, ContentType, FileSize, StoragePath, ExpiresAt, DownloadCount, MaxDownloads, PasswordHash, OtpHash, BrandLogo, BrandMessage, BrandAccent, Status |
| `UploadSession` | `upload_sessions` | DeliveryId, UploadId (R2), ObjectKey (R2 multipart tracking) |
| `Subscription` | `subscriptions` | UserId (FK), Plan, Status (Active/Cancelled/Expired), BillingPeriod (monthly/yearly), CurrentPeriodStart, CurrentPeriodEnd, CancelledAt, CreatedAt, UpdatedAt |
| `PaymentIntent` | `payment_intents` | UserId (FK), Code (unique, `QSC-XXXXXX`), PayOSOrderCode (unique), Plan, Billing, Amount, Currency, Status (Pending/Completed/Expired), ExpiresAt (1h) |
| `TempShare` | `TempShares` | Legacy P2P share reference — no longer used (P2P shares create `Delivery` records) |
| `Chunk` | `chunks` | Per-chunk tracking (deprecated — R2 handles chunking client-side) |
| `Manifest` | `manifests` | Upload manifest metadata |
| `DownloadTracking` | `download_tracking` | Per-delivery download counters |
| `PaymentEvent` | `payment_events` | PayOS payment webhook events |
| `AnalyticsEvent` | `analytics_events` | Anonymous product events (visitor, upload, download, P2P, OTP, ad, signup) — no PII |

### Constants
- `Infrastructure/PlanTierLimits.cs` — plan limits: max file size, max expiry days, max downloads, monthly bytes, concurrency
- `Infrastructure/AuthConstants.cs` — cookie name, claim types, provider identifiers

### Base Types
- `BaseAuditEntity.cs` — `Id`, `CreatedAt` (default `= DateTime.UtcNow`), `UpdatedAt?`
- `AuditableEntityInterceptor` — EF Core `SaveChangesInterceptor` auto-sets `CreatedAt`/`UpdatedAt` on `EntityState.Added`/`Modified`. Never set these manually in code. Works with any entity that has a `CreatedAt` or `UpdatedAt` property (not just `BaseAuditEntity` descendants — handles `Manifest` too).

---

## Object Mapping (Mapperly)

**All entity→DTO mappings use Riok.Mapperly.** Mappers live in `Features/<Feature>/Shared/<Entity>Mapper.cs`.

```csharp
[Mapper]
public static partial class XxxMapper
{
    public static partial XxxResponse ToXxxResponse(this XxxEntity source);
}
```

- `FileMapper` — `FileMetadata` → `FileItemResponse` (with `MapPropertyFromSource` for `FolderName`)
- `FolderMapper` — `Folder` → `FolderItemResponse` (ignores `FileCount`)
- `UserMapper` — `User` → `ProfileResponse` (ignores `UpdatedAt`)

**Exceptions to Mapperly:**
- **LINQ `.Select()` projections** with server-side subqueries (e.g., `db.Files.Count(...)`) or computed expression fields — construct DTO inline. EF Core cannot translate Mapperly-generated methods to SQL.
- **Simple value-object responses** (`new SomeResponse(sessionId, chunkSize)`) that don't map from a single entity.

## Database

**PostgreSQL** via EF Core Npgsql.

Migrations under `Migrations/`. Key schema:
- `users` — Id (UUID PK), Name, Email, AvatarUrl, Plan, CreatedAt, UpdatedAt
- `user_logins` — Id, UserId (FK), Provider, ProviderKey
- `user_usages` — Id, UserId (FK), PeriodStart, TotalBytesUploaded, TransferCount
- `folders` — Id (UUID PK), Name (TEXT UNIQUE), CreatedAt
- `files` — Id (UUID PK), Filename, ContentType, Size, FolderId (FK), Tags, CreatedAt
- `deliveries` — ShareCode, FileName, ContentType, FileSize, StoragePath, ExpiresAt, PasswordHash, OtpHash, etc.
- `upload_sessions` — DeliveryId, UploadId (R2), ObjectKey
- `payment_events` — PayOS payment event records
- `subscriptions` — User subscription lifecycle (Active → Cancelled → Expired)
- `payment_intents` — Upgrade payment codes (`QSC-XXXXXX`) for webhook user matching

---

## Data Flow

### Upload (R2 Direct)
```
┌──────────┐       ┌──────────────┐       ┌──────────┐
│ Browser  │       │   QuickShare  │       │ R2 / DB  │
│ (Upload) │       │   (9000)      │       │          │
└────┬─────┘       └────┬─────────┘       └────┬─────┘
     │                   │                      │
     │── POST /api/uploads/start-r2 ───────────▶│── DB insert ──▶
     │◀─ presigned URLs ───────────────────────◀│
     │── PUT file parts ──▶ (direct R2 multipart S3 upload) ──▶
     │── POST /api/uploads/commit-r2 ──────────▶│── DB insert ──▶
     │◀─ shareLink+key ────────────────────────◀│
```

### Download
```
┌──────────┐       ┌──────────────┐       ┌──────────┐
│ Browser  │       │   QuickShare  │       │ R2 / DB  │
│(Receiver)│       │   (9000)      │       │          │
└────┬─────┘       └────┬─────────┘       └────┬─────┘
     │                   │                      │
     │── GET /d/{code} ──▶── download/info ────▶│── DB lookup ──▶
     │◀─ password gate ──◀─────────────────────◀│
     │── password ───────▶── password/verify ──▶│── DB verify ──▶
     │◀─ ready ──────────◀─────────────────────◀│
     │── download ───────▶── download API ─────▶│── R2 fetch ───▶
     │◀─ AES-GCM decrypt ◀── file stream ──────◀│
     │  (client-side)
```

### P2P Data Flow (WebRTC)
```
Sender Browser                 QuickShare (9000)            Receiver Browser
     │                              │                           │
     │── POST /api/shares/p2p ─────▶│                           │
     │◀─ shareCode + shareLink ─────│                           │
     │                              │                           │
     │── WebSocket /ws/connect ────▶│◀── connect (receiver) ───│
     │◀──────── peer-joined ────────│─────────────────────────▶│
     │                              │                           │
     │══ RTCPeerConnection offer ───│─── relay offer ──────────▶│
     │◀────────────────────────────│────────── answer ─────────│
     │── ICE candidates ───────────│─── relay ICE ────────────▶│
     │                              │                           │
     │═════ DataChannel opened ════════════════════════════════│ (direct P2P)
     │── file chunks (plaintext) ─────────────────────────────▶│
     │◀────────────────────────────────────────────── ack ─────│
```

---

## Auth Flow

```
Browser → GET /login → QuickShare (9000)
  → Google OAuth challenge → user authenticates with Google
  → OAuth callback → OnCreatingTicket:
      1. Extract claims (sub, email, name)
      2. Call UserRegistrationService.RegisterAsync() in-process
         → DB lookup/create User + UserLogin
      3. Set cookie claims (nameidentifier, email, name, avatar, plan)
  → Cookie set → redirect to original page
  
Subsequent requests:
  - Page requests (e.g. /files, /account): cookie → [Authorize] passes → page renders
  - API requests (e.g. /api/files): cookie → [Authorize] passes → endpoint executes
  - Unauthenticated page request → redirect /login
  - Unauthenticated API request → 401 JSON response
```

---

## Tests

`tests/QuickShare.Server.Tests/` — 4 test files, 29 tests (xUnit):
- `FakeStorageProviderTests.cs` — In-memory storage provider round-trip, delete, empty data
- `LocalDiskStorageProviderStubTests.cs` — NotSupportedException assertions for R2 methods
- `R2ObjectKeyTests.cs` — Object key formatting, special character encoding
- `R2StorageExceptionTests.cs` — Transient vs permanent error classification

---

## Key Configuration

- **Single app** — `DataProtection` uses `PersistKeysToFileSystem` + `SetApplicationName("QuickShare")`
- **Storage Mode** — R2 is default (`IStorageProvider` → `R2StorageProvider`); dev can switch to local disk
- **Plan Tiers** — Free (100MB/file, 2GB/mo, 5 transfers/mo, 7d expiry), Pro (1GB/file, 500GB/mo, 100 transfers/mo, 30d expiry), Studio (20GB/file, unlimited, unlimited, custom branding + API)
- **`hx-boost="true"`** — All internal links get SPA-like behavior via htmx
- **Theme** — DAUB grunge-dark (typewriter / ink-stained aesthetic), persisted in localStorage

---

## Directory Structure

```
src/QuickShare/
├── Infrastructure/       # IStorageProvider, LocalDiskStorageProvider, Storage/ (R2), PlanTierLimits, AuthConstants, Jobs/ (Hangfire recurring jobs + dashboard auth)
├── Components/            # Layout/ (MainLayout, HtmxorLayout)
├── Data/                  # AppDbContext, AuditableEntityInterceptor, Entities/
├── Extensions/            # ClaimsPrincipalExtensions, ResultExtensions
├── Features/              # FastEndpoints + Mediator handlers
│   ├── Deliveries/        # Brand, List, Password, Otp, DeliveryService
│   ├── Downloads/         # Download
│   ├── Files/             # Move + Shared mappers (List handler in-process)
│   ├── Folders/           # Create, Update, Delete + Shared mappers (List/GetInfo handlers in-process)
│   ├── Payments/          # PayOS webhook, SubscriptionService, BillingHistoryService
│   ├── Reports/           # Abuse reporting
│   ├── Shares/            # P2P share creation
│   ├── Uploads/           # StartR2, CommitR2, AbortR2 + Shared/ (ManifestEncryption, ManifestData)
│   ├── Users/             # Avatar endpoint, Shared/ (UserRegistrationService, UserMapper)
│   ├── Uploads/           # R2 multipart (StartR2, CommitR2, AbortR2)
│   ├── Analytics/         # Anonymous analytics: AnalyticsService, track endpoint, dashboard query (issue #24)
│   └── Users/             # UserRegistrationService, UserMapper
├── Infrastructure/        # Storage, Quota, Options, ExceptionHandler, HtmxCacheMiddleware, RateLimitingMiddleware, P2PConnectionManager, P2PSignalingMiddleware
├── Middleware/            # (empty — middleware lives in Infrastructure/)
├── Migrations/            # EF Core migrations
├── Models/                # Entity models, DTOs (User, Delivery, Folder, etc.)
├── Pages/                 # 11 Blazor SSR pages (.razor + .razor.css)
├── Services/              # TrialService
├── wwwroot/               # Static assets (js/, css/, lib/, avatars/)
├── App.razor              # Root HTML shell
├── Routes.razor           # Blazor router
├── Program.cs             # Host builder + middleware pipeline
└── QuickShare.csproj      # Project file
```
