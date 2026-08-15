# US-CAP-3 — Recipient download without account

Source: `SPEC-quickshare-features` → CAP-3

**Story**
As a recipient, I want to open a share link on any device and download the file without creating an account, so that I can get the file with zero friction.

**Acceptance criteria**
- [ ] A fresh browser (no session) downloads the file in one click.
- [ ] No login wall, no sign-up prompt blocks the download.
- [ ] Works on mobile and desktop browsers.
- [ ] Download endpoint is public: FastEndpoints `AllowAnonymous()` explicit (no manual `IsAuthenticated` gate).
- [ ] Expired links render the expiration state (see CAP-5), not a broken page.

**Constraints in force**
- `AllowAnonymous()` explicit for delivery download (FastEndpoints auth-by-default).
- Download info cache TTL 30 s; invalidation on mutation.
- No recipient accounts (non-goal) — anonymous download only.

**Test anchors**
- `DownloadNoScriptTests` — no-JS default phase works.
- E2E: fresh-browser one-click download on mobile viewport.

---

## Implementation Flow

1. **Landing render** — `GET /d/{ShareCode}` → `Download.razor` (`[StreamRendering]`, MainLayout). `InitialPhase` = `HasOtp ? "otp" : "file"` — the *initial* phase always renders server-side (usable with no JS); the ad phase is never the server-rendered initial phase. Shell renders instantly; `download.js` drives phase transitions client-side.
2. **Info bootstrap** — `DownloadInfoHandler.GetAsync(shareCode, ct)` → FusionCache `download:info:{ShareCode}` TTL 30s; returns `DeliveryDownloadInfoResponse` (fileName/fileSize/mime/hasOtp/isEncrypted/expiry + senderPlan `Sender?.Plan ?? Anonymous`); errors mapped: `DeliveryNotFound` → 404, `DeliveryExpired` → 410, `DownloadLimitReached` → 410; page renders error/expired/locked states accordingly.
3. **Phase machine (`download.js`)** — `checking → otp → ad → file`; reads i18n from JSON block `download-i18n` and info from `download-info-data`; OTP submit → POST `/api/deliveries/{ShareCode}/verify-otp` (wrong → 400 "incorrect-otp" error state); ad phase → `QuickShareAds.init` + reward prompt route.
4. **No-JS fallback** — download anchor `onclick="return downloadApp.handleDownload(event)"`; when `!isEncrypted && !verifiedOtp` the handler returns `true` so the browser navigates natively to the endpoint (one-click, no account).
5. **File delivery** — `DownloadDeliveryHandler` (endpoint explicitly `AllowAnonymous()` — FastEndpoints secure-by-default) writes the HTTP response directly:
   - null delivery → 404; `ExpiresAt < now` → 410; OTP mismatch (bcrypt verify) → 403; `DownloadCount >= MaxDownloads` → 403; no StoragePath → 500.
   - Full download → R2 presigned GET (7-day) + `DownloadCount++` persisted; `Range` request → 1-hour presigned URL, **no** count increment.
6. **Expired UX (hand-off to CAP-5)** — handler returns 410 → page renders Expired state (filename + expiry), never a broken page.

## Flow Diagram

```mermaid
sequenceDiagram
    autonumber
    participant R as Recipient Browser
    participant W as Web Server (Download.razor)
    participant C as FusionCache (TTL 30s)
    participant H as DownloadDeliveryHandler
    participant R2 as R2 Storage
    R->>W: GET /d/{ShareCode}
    W->>C: GetAsync download:info:{code}
    C-->>W: DeliveryDownloadInfoResponse (fileName, hasOtp, isEncrypted, senderPlan, expiry)
    W-->>R: Server-rendered shell + download-info-data (initial phase: otp | file — no-JS)
    opt OTP gate (hasOtp)
        R->>W: POST /api/deliveries/{code}/verify-otp
        W-->>R: 200 verified / 400 incorrect-otp
    end
    opt Free-plan ad gate
        R->>W: Ad countdown 5s → file phase
    end
    R->>R: handleDownload: no-JS → native nav; encrypted → fetchBytes
    R->>H: GET /api/downloads/{code} (encrypted) or native navigation
    H-->>R: 404 not-found / 410 expired / 403 limit-OTP / 500 no-storage
    H->>R2: Presigned GET (7-day) + DownloadCount++
    R2-->>H: bytes
    H-->>R: file (Range → 1h URL, no count increment)
```

## Code Structure

- `Features/Downloads/Download/DownloadDeliveryEndpoint.cs` + `DownloadDeliveryHandler.cs` — public pipe to R2.
- `Features/Downloads/Info/DownloadInfoHandler.cs` — 30s cached info + error mapping.
- `Features/Deliveries/Otp/{VerifyOtpEndpoint,VerifyOtpHandler}.cs` — OTP gate.
- `Components/Pages/Download.razor` + `wwwroot/js/download.js` — phases + no-JS fallback.
- `Infrastructure/Caching/FusionCache` setup in `Program.cs` (`.Cache(30s)` per call).
