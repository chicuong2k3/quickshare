# QuickShare — Architecture

## Projects

| Project | Role | Port | Framework |
|---------|------|------|-----------|
| `QuickShare` | Monolith: FastEndpoints API + Blazor Static SSR | `9000` | .NET 10, FastEndpoints, Wolverine, EF Core, Npgsql, Htmxor, DAUB |
| `QuickShare.Querying` | Dynamic sort/paging engine + source generators | — | .NET 10 |
| `QuickShare.Server.Tests` | Unit tests | — | xUnit |

> **July 2026:** `QuickShare.Server` (API backend, port 59155) and `QuickShare.Web` (Blazor frontend, port 9000) merged into a single `QuickShare` monolith. `QuickShare.Shared` models/constants also folded in. Removed Refit, `IQuickShareApi`, `CookieForwardingHandler` — SSR pages now call `IMessageBus` or `AppDbContext` in-process. JS API calls use relative URLs (same origin, no CORS).

---

## Server Features

### Auth
- **Cookie auth** with dual behavior: `/api/*` paths return 401 (API behavior), other paths redirect `/login` (page behavior)
- **Google OAuth** — `OnCreatingTicket` calls `UserRegistrationService.RegisterAsync()` in-process (DRY with `RegisterUserEndpoint`)
- `ClaimsPrincipalExtensions.cs` — User ID extraction via `GetUserAsync(db, ct)`
- **Dev auto-signin** middleware — creates `dev-user` claims for local development

### Uploads (`Features/Uploads/`)
- **R2 Multipart** (`StartR2`, `CommitR2`, `AbortR2`) — Cloudflare R2 presigned multipart upload flow
- **Chunked** (`Start`, `Commit`, `Chunks`, `Status`) — Legacy chunk-by-chunk upload fallback
- `UploadConstants.cs` — Upload limits and magic numbers

### Downloads (`Features/Downloads/`)
- **Info** (`DownloadInfoEndpoint` + `Handler`) — File metadata with download gates:
  - Password gate (if `HasPassword`)
  - Payment gate (if `HasPayment` → VietQR payment)
  - Ad interstitial (free plan)
  - Download limit enforcement
- **Download** (`DownloadDeliveryEndpoint` + `Handler`) — Stream file from storage to client, supports AES-GCM client-side decryption hint

### Files (`Features/Files/`)
- **List** — Paginated `GET /api/files` with search (`?search=`) and folder filter (`?folderId=`)
- **Move** — `PATCH /api/files/{id}/folder` — drag-drop between folders
- **Delete** — `DELETE /api/files/{id}` — removes file from DB
- **Download** — Direct file download by ID
- **GetInfo** — Single file metadata
- `Shared/Errors.cs` — File-specific error types
- `Shared/FileMapper.cs` — Mapperly source-gen mapper (FileMetadata → FileItemResponse)

### Folders (`Features/Folders/`)
| Endpoint | Method | Path |
|----------|--------|------|
| Create | POST | `/api/folders` |
| List | GET | `/api/folders` |
| GetInfo | GET | `/api/folders/{id}` |
| Update | PUT | `/api/folders/{id}` |
| Delete | DELETE | `/api/folders/{id}` |

### Deliveries (`Features/Deliveries/`)
- **List** — `GET /api/deliveries` — user's delivery history
- **Brand** — `GET/PUT /api/deliveries/{id}/brand` — custom delivery branding (logo URL, message, accent color)
- **Password** — `POST /api/deliveries/{code}/verify` — verify password-protected delivery
- **Extend** — Extend delivery expiry timestamp
- `DeliveryService.cs` — Shared delivery coordination logic

### Payments (`Features/Payments/`)
- **VietQR** — `POST /api/deliveries/{code}/payment/vietqr` — generate VietQR payment code
- **VerifyPayment** — `GET /api/deliveries/{code}/payment` — poll payment status
- **Kofi** — `POST /api/payments/kofi` — Ko-fi donation webhook, billing history, upgrade initiation

### Reports (`Features/Reports/`)
- `ReportDeliveryEndpoint` + `Handler` — `POST /api/deliveries/{code}/report` — abuse reporting (spam, malware, abuse, other)

### Shares (`Features/Shares/`)
- **P2P** — `POST /api/shares/p2p` — create P2P share code + WebRTC signaling metadata

### Users (`Features/Users/`)
- **Profile** — `GET/PUT /api/users/me/profile` — name, avatar URL CRUD
- **Register** — `POST /api/users/register` — idempotent user creation
- `Shared/UserRegistrationService.cs` — Extracted registration logic used by both `RegisterUserEndpoint` and Google OAuth callback (DRY)

---

## Infrastructure

| Component | Purpose |
|-----------|---------|
| **PostgreSQL** | Users, logins, files metadata, folders, deliveries, usage tracking. EF Core + Npgsql |
| **Cloudflare R2** (`R2StorageProvider.cs`) | S3-compatible file blob storage, presigned URLs, multipart upload |
| **Local Disk** (`LocalDiskStorageProvider.cs`) | Fallback/development disk storage |
| **Wolverine** | Message bus + transactional outbox for background job reliability |
| **FastEndpoints** | REPR-pattern (Request-Endpoint-Response) minimal API framework |
| **FastEndpoints Swagger** | OpenAPI/Swagger document generation at `/swagger` |
| **QuotaEnforcer** | Per-plan limits: file size caps, monthly upload quota, transfer counts |
| **DataProtection** | `PersistKeysToFileSystem` with `SetApplicationName("QuickShare")` — single app, no cross-process key sharing needed |
| **Background Jobs** | 3 hosted services: `DeliveryCleanupService`, `ZombiePartSweeper`, `DeliveryExpiryJob` |
| **TrialService** | 14-day free trial tracking |
| **GlobalExceptionHandler** | Centralized `ProblemDetails` error responses |
| **RequestLoggingMiddleware** | Structured inbound request logging |
| **P2P Signaling** | `P2PConnectionManager` (singleton) + `P2PSignalingMiddleware` — WebSocket-based WebRTC signaling relay |
| **Rate Limiting** | `RateLimitingMiddleware` — configurable request throttling |

---

## Blazor SSR Pages

| Route | Page | Interactivity | Key Features |
|-------|------|--------------|-------------|
| `/` | `Upload.razor` | Vanilla JS | Drag-drop + paste upload, AES-256-GCM client-side encryption, R2 multipart, QR code generation, expiry selector (24h/7d) |
| `/d/{code}` | `Download.razor` | Vanilla JS | Multi-phase download: password gate → VietQR payment gate → ad interstitial (free plan) → AES-GCM decrypt → file save. ZK attestation panel, abuse reporting form, sender branding |
| `/p2p/send` | `P2PTransfer.razor` | Vanilla JS + WebRTC | P2P sender: create share code, WebRTC signaling (offer/answer), PIN-protected encryption, data channel file streaming with progress |
| `/p2p/d/{code}` | `P2PDownload.razor` | Vanilla JS + WebRTC | P2P receiver: signaling connect, PIN gate, WebRTC answer, data channel file receipt, progress tracking |
| `/files` | `Files.razor` | Vanilla JS + htmx | File browser: stats dashboard (4 cards), folder strip with drag-drop file move, CRUD modals (create/rename/delete folder), htmx-powered live search, download button per file |
| `/account` | `Account.razor` | Vanilla JS + htmx | Profile edit (name, avatar upload with preview), OAuth provider badge, delivery branding manager (logo/message/accent per delivery), htmx form submission |
| `/account/billing` | `AccountBilling.razor` | htmx | Billing history list with pagination |
| `/upgrade` | `Upgrade.razor` | htmx | Plan upgrade page, Ko-fi payment integration |
| `/login` | `Login.razor` | Server-side | OAuth provider buttons: Google, GitHub, Facebook, TikTok |
| `/pricing` | `Pricing.razor` | htmx | 3-tier plan cards (Free/Pro/Studio), monthly/yearly billing toggle via `hx-get` + `HtmxFragment`, FAQ accordion, CTA section |

### SSR Data Access Pattern

SSR pages that previously called the API via HTTP (`IQuickShareApi`/Refit) now access data **in-process**:

| Page | Old (HTTP) | New (in-process) |
|------|-----------|------------------|
| `Files.razor` | `Api.GetFoldersAsync()`, `Api.GetFilesAsync()`, `Api.GetAccountProfileAsync()` | Inject `IMessageBus`, call `ListFoldersQuery`, `ListFilesQuery`, `GetUserProfileEndpoint` query records directly |
| `Account.razor` | `Api.GetMyProfileAsync()`, `Api.UpdateMyProfileAsync()` | Inject `IMessageBus` / `AppDbContext`, call `GetUserProfileEndpoint` / `UpdateUserProfileEndpoint` query/command records |
| `AccountBilling.razor` | `Api.GetBillingHistoryAsync()` | Inject `IMessageBus`, call `GetBillingHistoryEndpoint` query record |
| `Upgrade.razor` | `Api.GetMyProfileAsync()` | Inject `IMessageBus`, call `GetUserProfileEndpoint` query record |

**Rule:** If the endpoint's handler goes through `IMessageBus.InvokeAsync<Result<T>>(cmd)`, the page injects `IMessageBus` and calls the same command/query. If the endpoint uses `AppDbContext` inline, the page injects `AppDbContext` and queries directly with Mapperly mappers.

JS-based pages (Upload, Download, P2PTransfer, P2PDownload) call `/api/*` via `fetch()`/`XMLHttpRequest` — these use **relative URLs** (empty `apiBaseUrl` in `window.ZAPDROP_CONFIG`), so they work same-origin with no CORS.

### Component Layout
- **`MainLayout.razor`** — Flex-column layout: header (logo + nav) → `@Body` → footer. Inherits `HtmxLayoutComponentBase` for htmx partial rendering
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
| **P2P** | Custom `RTCPeerConnection` + data channels (`/wwwroot/js/p2p/`): `signaling.js`, `peerConnection.js`, `fileTransfer.js`, `fileReceiver.js`, `encryption.js` |
| **QR** | `qrcode-generator` (npm vendor-copied to `wwwroot/lib/qrcode/qrcode.min.js`) |
| **API** | JS pages call relative `/api/*` (same origin); SSR pages use in-process `IMessageBus` or `AppDbContext` |
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

## Data Models & Constants (`Models/`, `Constants/`)

Models, DTOs, and constants now live directly in `src/QuickShare/Models/` and `src/QuickShare/Constants/`.

### Data Models

| Entity | Table | Key Fields |
|--------|-------|-----------|
| `User` | `users` | Id (Guid v7), Name, Email, AvatarUrl, Plan (Free/Pro/Studio), CreatedAt, UpdatedAt |
| `UserLogin` | `user_logins` | Id, UserId (FK), Provider (google/github/facebook/tiktok), ProviderKey |
| `UserUsage` | `user_usages` | Id, UserId (FK), PeriodStart (month), TotalBytesUploaded, TransferCount |
| `Folder` | `folders` | Id (UUID), Name (UNIQUE) |
| `FileMetadata` | `files` | Id (UUID), Filename, ContentType, Size, FolderId (FK), Tags (TEXT[]), CreatedAt |
| `Delivery` | `deliveries` | ShareCode, FileName, ContentType, FileSize, ObjectKey (R2), ExpiresAt, DownloadCount, MaxDownloads, HasPassword, HasPayment, Price, PlanTier |
| `UploadSession` | `upload_sessions` | DeliveryId, UploadId (R2), ObjectKey (R2 multipart tracking) |
| `TempShare` | `temp_shares` | Short-lived encrypted P2P share with auto-expiry |
| `Chunk` | `chunks` | Per-chunk tracking for legacy chunked upload |
| `Manifest` | `manifests` | Upload manifest metadata |
| `DownloadTracking` | `download_tracking` | Per-delivery download counters |
| `PaymentEvent` | `payment_events` | Ko-fi payment webhook events |

### Constants
- `PlanTierLimits.cs` — Plan-specific limits (file size, monthly storage, transfer count)
- `AuthConstants.cs` — Cookie name, scheme constants, provider identifiers

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
- `deliveries` — ShareCode, FileName, ContentType, FileSize, ObjectKey, ExpiresAt, etc.
- `upload_sessions` — DeliveryId, UploadId (R2), ObjectKey
- `payment_events` — Ko-fi webhook data

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
     │◀─ payment QR ─────◀── vietQR ───────────▶│── VietQR ─────▶
     │── payment status ─▶── payment poll ─────▶│── DB check ───▶
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
     │── file chunks (encrypted) ─────────────────────────────▶│
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
├── Abstractions/          # IStorageProvider interface
├── BackgroundJobs/        # DeliveryExpiryJob, ZombiePartSweeper
├── Components/            # Layout/ (MainLayout, HtmxorLayout)
├── Constants/             # PlanTierLimits, AuthConstants
├── Data/                  # AppDbContext, AuditableEntityInterceptor
├── Extensions/            # ClaimsPrincipalExtensions, ResultExtensions
├── Features/              # FastEndpoints + Wolverine handlers
│   ├── Deliveries/        # Brand, Extend, List, Password, DeliveryService
│   ├── Downloads/         # Download, Info
│   ├── Files/             # CRUD + Shared mappers
│   ├── Folders/           # CRUD + Shared mappers
│   ├── Payments/          # Kofi, VietQR
│   ├── Reports/           # Abuse reporting
│   ├── Shares/            # P2P share creation
│   ├── Uploads/           # R2 + chunked upload
│   └── Users/             # Profile, Register, UserRegistrationService, UserMapper
├── Infrastructure/        # Storage (R2 + LocalDisk), Quota, Logging, Options, ExceptionHandler
├── Middleware/            # P2PConnectionManager, P2PSignaling, RateLimiting
├── Migrations/            # EF Core migrations
├── Models/                # Entity models, DTOs (User, Delivery, Folder, etc.)
├── Pages/                 # 10 Blazor SSR pages (.razor + .razor.css)
├── Services/              # TrialService
├── wwwroot/               # Static assets (js/, css/, lib/, avatars/)
├── App.razor              # Root HTML shell
├── Routes.razor           # Blazor router
├── Program.cs             # Host builder + middleware pipeline
└── QuickShare.csproj      # Project file
```
