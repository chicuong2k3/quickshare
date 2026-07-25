# QuickShareCloud — Architecture

## Projects

| Project | Role | Port | Framework |
|---------|------|------|-----------|
| `QuickShare.Server` | REST API backend | `59155` | .NET 10, FastEndpoints, Wolverine, EF Core, Npgsql |
| `QuickShare.Web` | Blazor Static SSR frontend | `9000` | .NET 10, Htmxor, DAUB, htmx |
| `QuickShare.Shared` | Shared models, DTOs, constants | — | .NET 10 |
| `QuickShare.Querying` | Dynamic sort/paging engine + source generators | — | .NET 10 |
| `QuickShare.Server.Tests` | Server unit tests | — | xUnit |

---

## Server (`QuickShare.Server`)

### API Features

#### Auth
- **Google OAuth** — Server-side cookie auth via `AddAuthentication().AddGoogle()`
- `AuthConstants.cs` — Cookie name, scheme constants
- `ApiCookieAuthExtensions.cs` — Extension helpers
- `ClaimsPrincipalExtensions.cs` — User ID extraction
- `GoogleLoginEndpoint.cs` — Custom Google login handler with auto-registration

#### Uploads (`Features/Uploads/`)
- **R2 Multipart** (`StartR2`, `CommitR2`, `AbortR2`) — Cloudflare R2 presigned multipart upload flow
- **Chunked** (`Start`, `Commit`, `Chunks`, `Status`) — Legacy chunk-by-chunk upload fallback
- `UploadConstants.cs` — Upload limits and magic numbers

#### Downloads (`Features/Downloads/`)
- **Info** (`DownloadInfoEndpoint` + `Handler`) — File metadata with download gates:
  - Password gate (if `HasPassword`)
  - Payment gate (if `HasPayment` → VietQR payment)
  - Ad interstitial (free plan)
  - Download limit enforcement
- **Download** (`DownloadDeliveryEndpoint` + `Handler`) — Stream file from storage to client, supports AES-GCM client-side decryption hint

#### Files (`Features/Files/`)
- **List** — Paginated `GET /api/files` with search (`?search=`) and folder filter (`?folderId=`)
- **Move** — `PATCH /api/files/{id}/folder` — drag-drop between folders
- **Delete** — `DELETE /api/files/{id}` — removes file from DB
- **Download** — Direct file download by ID
- **GetInfo** — Single file metadata
- `Shared/Errors.cs` — File-specific error types
- `Shared/FileMapper.cs` — Mapperly source-gen mapper (FileMetadata → FileItemResponse)

#### Folders (`Features/Folders/`)
| Endpoint | Method | Path |
|----------|--------|------|
| Create | POST | `/api/folders` |
| List | GET | `/api/folders` |
| GetInfo | GET | `/api/folders/{id}` |
| Update | PUT | `/api/folders/{id}` |
| Delete | DELETE | `/api/folders/{id}` |

#### Deliveries (`Features/Deliveries/`)
- **List** — `GET /api/deliveries` — user's delivery history
- **Brand** — `GET/PUT /api/deliveries/{id}/brand` — custom delivery branding (logo URL, message, accent color)
- **Password** — `POST /api/deliveries/{code}/verify` — verify password-protected delivery
- **Extend** — Extend delivery expiry timestamp
- `DeliveryService.cs` — Shared delivery coordination logic

#### Payments (`Features/Payments/`)
- **VietQR** — `POST /api/deliveries/{code}/payment/vietqr` — generate VietQR payment code
- **VerifyPayment** — `GET /api/deliveries/{code}/payment` — poll payment status
- **KofiWebhook** — `POST /api/payments/kofi` — Ko-fi donation webhook

#### Reports (`Features/Reports/`)
- `ReportDeliveryEndpoint` + `Handler` — `POST /api/deliveries/{code}/report` — abuse reporting (spam, malware, abuse, other)

#### Shares (`Features/Shares/`)
- **P2P** — `POST /api/shares/p2p` — create P2P share code + WebRTC signaling metadata

#### TempShares (`Features/TempShares/`)
- **Create** / **Get** / **Download** — Short-lived encrypted temporary shares with auto-expiry

#### Users (`Features/Users/`)
- **Profile** — `GET/PUT /api/users/me/profile` — name, avatar URL CRUD
- `ProfileResponse.cs` — includes Plan tier, email, avatar

---

### Server Infrastructure

| Component | Purpose |
|-----------|---------|
| **PostgreSQL** | Users, logins, files metadata, folders, deliveries, usage tracking. EF Core + Npgsql |
| **Cloudflare R2** (`R2StorageProvider.cs`) | S3-compatible file blob storage, presigned URLs, multipart upload |
| **Local Disk** (`LocalDiskStorageProvider.cs`) | Fallback/development disk storage |
| **Wolverine** | Message bus + transactional outbox for background job reliability |
| **FastEndpoints** | REPR-pattern (Request-Endpoint-Response) minimal API framework |
| **FastEndpoints Swagger** | OpenAPI/Swagger document generation |
| **QuotaEnforcer** | Per-plan limits: file size caps, monthly upload quota, transfer counts |
| **DataProtection** | Shared keyring with Web (`PersistKeysToFileSystem`, `SetApplicationName("QuickShare")`) |
| **CORS** | Configurable allowed origins for API calls from Web |
| **Background Jobs** | 3 hosted services: `DeliveryCleanupService`, `ZombiePartSweeper`, `DeliveryExpiryJob` |
| **TrialService** | 14-day free trial tracking |
| **GlobalExceptionHandler** | Centralized `ProblemDetails` error responses |
| **RequestLoggingMiddleware** | Structured inbound request logging |

---

## Web (`QuickShare.Web`)

### Pages

| Route | Page | Interactivity | Key Features |
|-------|------|--------------|-------------|
| `/` | `Upload.razor` | Vanilla JS | Drag-drop + paste upload, AES-256-GCM client-side encryption, R2 multipart, QR code generation, expiry selector (24h/7d) |
| `/d/{code}` | `Download.razor` | Vanilla JS | Multi-phase download: password gate → VietQR payment gate → ad interstitial (free plan) → AES-GCM decrypt → file save. ZK attestation panel, abuse reporting form, sender branding |
| `/p2p/send` | `P2PTransfer.razor` | Vanilla JS + WebRTC | P2P sender: create share code, WebRTC signaling (offer/answer), PIN-protected encryption, data channel file streaming with progress |
| `/p2p/d/{code}` | `P2PDownload.razor` | Vanilla JS + WebRTC | P2P receiver: signaling connect, PIN gate, WebRTC answer, data channel file receipt, progress tracking |
| `/files` | `Files.razor` | Vanilla JS + htmx | File browser: stats dashboard (4 cards), folder strip with drag-drop file move, CRUD modals (create/rename/delete folder), htmx-powered live search, download button per file |
| `/account` | `Account.razor` | Vanilla JS + htmx | Profile edit (name, avatar upload with preview), OAuth provider badge, delivery branding manager (logo/message/accent per delivery), htmx form submission |
| `/login` | `Login.razor` | Server-side | OAuth provider buttons: Google, GitHub, Facebook, TikTok |
| `/pricing` | `Pricing.razor` | htmx | 3-tier plan cards (Free/Pro/Studio), monthly/yearly billing toggle via `hx-get` + `HtmxFragment`, FAQ accordion, CTA section |

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
| **API Client** | Typed `HttpClient` via `IHttpClientFactory` (`ApiClient.cs`) |
| **Auth** | `AuthorizeRouteView` + `[Authorize]` on protected pages; `CookieForwardingHandler` for API calls |
| **Auth** | Shared cookie auth via DataProtection keyring; `CookieForwardingHandler` for Web→Server API calls |
| **Anti-forgery** | `app.UseAntiforgery()` + `app.UseHtmxAntiforgery()` |

### Static Assets (`wwwroot/`)
```
wwwroot/
├── js/
│   ├── upload.js            # R2 multipart + AES-GCM encryption + QR renderer
│   └── p2p/
│       ├── signaling.js     # WebSocket signaling client
│       ├── peerConnection.js # RTCPeerConnection wrapper
│       ├── fileTransfer.js  # FileSender (chunked data channel)
│       ├── fileReceiver.js  # FileReceiver (chunked data channel)
│       └── encryption.js    # AES-GCM key derivation + encrypt/decrypt
├── lib/
│   └── qrcode/
│       └── qrcode.min.js    # QR code generator (from npm)
└── avatars/                 # User-uploaded avatar images
```

---

## Shared (`QuickShare.Shared`)

---

### Account/Profile Flow

```
Browser → GET /account (cookie) → Web (9000)
  → OnInitializedAsync: GET /api/users/me/profile → Server (59155)
  → Server decrypts cookie → GetOrCreateUserAsync() → DB lookup/create
  → Returns ProfileResponse { Name, Email, AvatarUrl, Plan }
  → Web renders Account.razor with profile data + branding manager

Profile Update (htmx):
  POST /account (hx-post, hx-target="#profile-card")
  → HandleUpdateProfile → PUT /api/users/me/profile → Server
  → Server updates User.Name + User.AvatarUrl
  → Web re-signs cookie → returns updated #profile-card HTML
```

---

### Data Models

| Entity | Table | Key Fields |
|--------|-------|-----------|
| `User` | `users` | Id (Guid v7), Name, Email, AvatarUrl, Plan (Free/Pro/Studio), CreatedAt, UpdatedAt |
| `UserLogin` | `user_logins` | Id, UserId (FK), Provider (google/github/facebook/tiktok), ProviderKey |
| `UserUsage` | `user_usages` | Id, UserId (FK), PeriodStart (month), TotalBytesUploaded, TransferCount |
| `Folder` | `folders` | Id (UUID), Path (UNIQUE, e.g. `/` or `/Projects`) |
| `FileMetadata` | `files` | Id (UUID), Filename, ContentType, Size, FolderId (FK), Tags (TEXT[]), CreatedAt |
| `Delivery` | `deliveries` | ShareCode, FileName, ContentType, FileSize, ObjectKey (R2), ExpiresAt, DownloadCount, MaxDownloads, HasPassword, HasPayment, Price, PlanTier |
| `UploadSession` | `upload_sessions` | DeliveryId, UploadId (R2), ObjectKey (R2 multipart tracking) |
| `TempShare` | `temp_shares` | Short-lived encrypted P2P share with auto-expiry |
| `Chunk` | `chunks` | Per-chunk tracking for legacy chunked upload |
| `Manifest` | `manifests` | Upload manifest metadata |
| `DownloadTracking` | `download_tracking` | Per-delivery download counters |

### Constants
- `PlanTierLimits.cs` — Plan-specific limits (file size, monthly storage, transfer count)

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

Schema DDL at `schema.sql`:
- `folders` — id (UUID PK), path (TEXT UNIQUE), created_at
- `files` — id (UUID PK), filename, content_type, size, folder_id (FK), tags, created_at
- Indexes on `folder_id`, `filename`, `created_at DESC`

---

## Data Flow

```
┌──────────┐       ┌──────────┐       ┌──────────┐       ┌──────────┐
│ Browser  │       │   Web    │       │  Server  │       │ R2 / DB  │
│ (Upload) │       │ (9000)   │       │ (59155)  │       │          │
└────┬─────┘       └────┬─────┘       └────┬─────┘       └────┬─────┘
     │                   │                  │                    │
     │── POST multipart ─▶── start-r2 API ──▶── presigned URLs ──▶
     │◀─ presigned URLs ─◀─────────────────◀────────────────────│
     │── PUT file parts ──▶ (direct R2 multipart S3 upload) ────▶
     │── POST commit-r2 ──▶── commit-r2 API ──▶── DB insert ────▶
     │◀─ shareLink+key ──◀─────────────────◀────────────────────│
     │                                                           
     │  🔗 share link sent to receiver                           
     │                                                           
┌────┴─────┐       ┌────┬─────┐       ┌────┬─────┐       ┌────┬─────┐
│ Browser  │       │   Web    │       │  Server  │       │ R2 / DB  │
│(Receiver)│       │ (9000)   │       │ (59155)  │       │          │
└────┬─────┘       └────┬─────┘       └────┬─────┘       └────┬─────┘
     │                   │                  │                    │
     │── GET /d/{code} ──▶── download/info ─▶── DB lookup ─────▶
     │◀─ password gate ──◀─────────────────◀────────────────────│
     │── password ───────▶── password/verify▶── DB verify ──────▶
     │◀─ payment QR ─────◀── vietQR ───────▶── VietQR generate ─▶
     │── payment status ─▶── payment poll ──▶── DB check ───────▶
     │◀─ ready ──────────◀─────────────────◀────────────────────│
     │── download ───────▶── download API ──▶── R2 fetch ───────▶
     │◀─ AES-GCM decrypt ◀─────────────────◀── file stream ────◀│
     │  (client-side)                                              │
```

### P2P Data Flow (WebRTC)
```
Sender Browser                      Server (59155)              Receiver Browser
     │                                    │                           │
     │── POST /api/shares/p2p ───────────▶│                           │
     │◀─ shareCode + shareLink ───────────│                           │
     │                                    │                           │
     │── WebSocket signaling connect ─────▶│◀── connect (receiver) ──│
     │◀────────── peer-joined ────────────│─────────────────────────▶│
     │                                    │                           │
     │── RTCPeerConnection offer ─────────│─── relay offer ──────────▶│
     │◀──────────────────────────────────│────────── answer ─────────│
     │── ICE candidates ─────────────────│─── relay ICE ────────────▶│
     │                                    │                           │
     │═══════ DataChannel opened ════════════════════════════════════│ (direct P2P)
     │── file chunks (encrypted) ───────────────────────────────────▶│
     │◀────────────────────────────────────────────────── ack ───────│
```

---

## Tests

`tests/QuickShare.Server.Tests/` — 4 test files (xUnit):
- `FakeStorageProviderTests.cs`
- `LocalDiskStorageProviderStubTests.cs`
- `R2ObjectKeyTests.cs`
- `R2StorageExceptionTests.cs`

---

## Key Configuration

- **Shared DataProtection** — Both Server and Web use same `ApplicationName` + key path for cookie sharing
- **Storage Mode** — R2 is default (`IStorageProvider` → `R2StorageProvider`); dev can switch to local disk
- **Plan Tiers** — Free (100MB/file, 2GB/mo, 5 transfers/mo, 7d expiry), Pro (1GB/file, 500GB/mo, 100 transfers/mo, 30d expiry), Studio (20GB/file, unlimited, unlimited, custom branding + API)
- **`hx-boost="true"`** — All internal links get SPA-like behavior via htmx
- **Theme** — DAUB grunge-dark (typewriter / ink-stained aesthetic), persisted in localStorage
