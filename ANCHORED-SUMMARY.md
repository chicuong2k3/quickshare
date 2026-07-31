# Anchored Summary — QuickShareCloud Endpoint Cleanup

## Objective
Audit all HTTP API endpoints, remove dead endpoints, and convert unnecessary Razor-facing endpoints to direct form handlers.

## Architecture Principle
- **Browser JS** (`upload.js`, `download.js`, `files.js`, `account.js`, `p2p/send.js`) → must call HTTP endpoints (cannot call server C# directly)
- **Blazor SSR pages** → call services directly or use `<form method="post">` handlers — NO intermediate API endpoint
- **External callers** (PayOS webhook) → must call HTTP endpoints

## Important Details
- **Stack**: .NET 10 Blazor Static SSR + FastEndpoints + Wolverine (IMessageBus) + EF Core
- **Auth**: Cookie auth with Google OAuth; FastEndpoints secure-by-default (no `AllowAnonymous()` = requires auth)
- **Blazor pages load data via**: `IMessageBus.InvokeAsync()` → handler → EF Core, or service classes directly
- **Blazor pages handle mutations via**: `<form method="post">` → `OnInitializedAsync` reads form → calls service directly

## Endpoint Status (33 original → 15 remaining)

### LIVE endpoints (15 — all called from browser JS or external webhook)

| Route | Endpoint Class | Called From |
|---|---|---|
| `POST /api/uploads/start-r2` | StartR2Endpoint | upload.js |
| `POST /api/uploads/commit-r2` | CommitR2Endpoint | upload.js |
| `POST /api/uploads/abort-r2` | AbortR2Endpoint | upload.js |
| `GET /api/deliveries` | ListDeliveriesEndpoint | account.js |
| `GET /api/deliveries/{ShareCode}/brand` | GetBrandEndpoint | account.js, download.js |
| `PUT /api/deliveries/{DeliveryId}/brand` | SetBrandEndpoint | account.js |
| `POST /api/deliveries/{ShareCode}/verify` | VerifyPasswordEndpoint | download.js |
| `POST /api/deliveries/{ShareCode}/report` | ReportDeliveryEndpoint | download.js |
| `GET /api/downloads/{ShareCode}` | DownloadDeliveryEndpoint | download.js |
| `PATCH /api/files/{fileId}/folder` | MoveFileEndpoint | files.js |
| `POST /api/folders` | CreateFolderEndpoint | files.js |
| `PUT /api/folders/{id}` | UpdateFolderEndpoint | files.js |
| `DELETE /api/folders/{id}` | DeleteFolderEndpoint | files.js |
| `POST /api/shares/p2p` | CreateP2PShareEndpoint | p2p/send.js |
| `POST /api/payments/payos/webhook` | PayOSWebhookEndpoint | External (PayOS webhook) |

### DEAD endpoints removed (18 total)

**16 — no consumers:**
- `POST /api/uploads/start` → StartUploadEndpoint — legacy chunked upload, replaced by R2
- `POST /api/uploads/{UploadId}/commit` → CommitUploadEndpoint — legacy chunked upload
- `POST /api/uploads/{UploadId}/chunks/{ChunkIndex:int}` → UploadChunkEndpoint — legacy chunked upload
- `GET /api/uploads/{UploadId}/status` → UploadStatusEndpoint — legacy chunked upload
- `GET /api/files` → ListFilesEndpoint — Blazor loads files via IMessageBus
- `GET /api/files/{id}` → GetFileInfoEndpoint — no consumers
- `DELETE /api/files/{id}` → DeleteFileEndpoint — no consumers
- `GET /api/folders` → ListFoldersEndpoint — Blazor uses IMessageBus
- `GET /api/folders/{id}` → GetFolderInfoEndpoint — Blazor uses IMessageBus
- `GET /api/downloads/{ShareCode}/info` → DownloadInfoEndpoint — Blazor uses IMessageBus
- `POST /api/users/register` → RegisterUserEndpoint — OAuth uses UserRegistrationService directly
- `GET /api/users/me/profile` → GetUserProfileEndpoint — Account.razor uses EF Core directly
- `PUT /api/users/me/profile` → UpdateUserProfileEndpoint — Account.razor uses form POST to server
- `GET /api/subscriptions/current` → GetSubscriptionEndpoint — AccountBilling uses SubscriptionService directly
- `GET /api/payments/history` → GetBillingHistoryEndpoint — AccountBilling uses BillingHistoryService directly
- `POST /api/deliveries/{DeliveryId}/extend` → ExtendDeliveryEndpoint — no consumers

**2 — converted to form handlers:**
- `POST /api/subscriptions/cancel` → CancelSubscriptionEndpoint → AccountBilling.razor `<form method="post">` handler calls `SubscriptionService.CancelAsync()` directly
- `POST /api/upgrade/initiate` → InitiateUpgradeEndpoint → Upgrade.razor `<form method="post">` handler creates PaymentIntent directly

## Completed Work

### 1. VietQR dead code removal
- Deleted `Features/Uploads/VietQR/` — entire directory (11 files)
- Deleted all references in `Program.cs`
- Deleted `VerifyPaymentEndpoint.cs`

### 2. Dead endpoint removal (18 endpoints)
- Deleted 9 entire feature directories (Uploads Start/Commit/Chunks/Status, Files GetInfo/Delete, Deliveries Extend, Users Register/Profile)
- Deleted 6 individual endpoint files (keeping shared handlers used by Blazor pages)
- Deleted 3 additional files (CancelSubscriptionEndpoint, InitiateUpgradeEndpoint, InitiateUpgradeValidator)

### 3. Razor-facing endpoints converted to form handlers
- **AccountBilling.razor**: Cancel button → `<form method="post" Action=cancel>` → `SubscriptionService.CancelAsync()`
- **Upgrade.razor**: Pay with PayOS button → `<form method="post" Plan/Billing>` → creates PaymentIntent + calls PayOS API → shows QR/checkout URL
- Both pages now handle mutations the same way they load data: via direct service calls, no HTTP round-trip

### 4. Shared handlers preserved
- `ListFilesHandler.cs` — used by Files.razor via IMessageBus
- `ListFoldersHandler.cs` — used by Files.razor via IMessageBus
- `GetFolderInfoHandler.cs` — used by Files.razor via IMessageBus
- `DownloadInfoHandler.cs` — used by Download.razor via IMessageBus
- `SubscriptionService.cs` — used by AccountBilling.razor, SubscriptionExpiryJob, PayOSWebhookEndpoint
- `BillingHistoryService.cs` — used by AccountBilling.razor

## Relevant Files
- `src/QuickShare/Pages/AccountBilling.razor` — form handler for subscription cancel
- `src/QuickShare/Pages/Upgrade.razor` — form handler for payment intent creation
- `src/QuickShare/wwwroot/js/upload.js` — calls start-r2, commit-r2, abort-r2
- `src/QuickShare/wwwroot/js/download.js` — calls download, verify, report, brand
- `src/QuickShare/wwwroot/js/files.js` — calls folder CRUD, file move
- `src/QuickShare/wwwroot/js/account.js` — calls delivery list, brand get/set
- `src/QuickShare/wwwroot/js/p2p/send.js` — calls share creation
