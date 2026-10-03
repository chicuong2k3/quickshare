# Analytics — Architecture (issue #24)

Anonymous product analytics: track what users do with the app, without collecting
PII. Feeds the founder dashboard at `/admin/analytics`.

## Principles

- **Anonymous by design** — events never store email, user id, or raw IP.
- **Visitor identity** is a random GUID cookie (`qs_visitor`, 400-day, HttpOnly).
  DAU counts **distinct visitor cookie ids**, never users.
- **Country only** — geo comes from the Cloudflare `CF-IPCountry` header. The raw
  IP is never persisted.
- **Kill switch** — `Analytics:Enabled` (default `true`); when off, every
  `RecordAsync` no-ops.
- **Never breaks the primary flow** — recording failures are logged and swallowed.

## Events

| Event | When | Where |
|-------|------|-------|
| `visitor` | Landing page load | Home.razor beacon → `POST /api/analytics/track` |
| `upload_started` | `start-r2` success | `StartR2Handler` |
| `upload_completed` | `commit-r2` success (link sent) | `CommitR2Handler` |
| `download_completed` | Full download served | `DownloadDeliveryHandler` (background task) |
| `p2p_session_created` | `POST /api/shares/p2p` success | `CreateP2PShareEndpoint` |
| `p2p_receiver_joined` | Receiver registers on signaling WS | `P2PSignalingMiddleware` |
| `otp_pass` / `otp_fail` | OTP verification result | `VerifyOtpHandler` |
| `ad_interstitial_viewed` | Download ad phase shown | download.ts beacon → track endpoint |
| `signup` | New user created (not existing login) | `UserRegistrationService.RegisterAsync` |

Event names are constants in `Features/Analytics/AnalyticsConstants.cs`
(`AnalyticsEvents`). The client beacon may only fire the allowlisted subset
(`visitor`, `ad_interstitial_viewed`).

## Storage

`analytics_events` table (entity `AnalyticsEvent`):

- `Id` (Guid v7), `EventName` (indexed), `VisitorId?`, `CountryCode?` (2 chars)
- `FileName?`, `FileSize?`, `ContentType?`, `Plan?` — non-PII context for
  top-files and plan-mix analysis
- `CreatedAt` set by `AuditableEntityInterceptor`

Indexes: `(EventName, CreatedAt)`, `(VisitorId, CreatedAt)`, `(CountryCode)`.

## Recording

`AnalyticsService` (scoped) — `RecordAsync(eventName, data?, ct)`:

1. Captures visitor cookie (creating it via `Set-Cookie` on first contact) +
   country header from the current `HttpContext`.
2. Writes one row through its **own short-lived scope**, so it is safe from
   request handlers, middleware, and background tasks alike.
3. Captured-context overload `RecordAsync(eventName, data, context, ct)` for
   background work where `HttpContext` is gone (download path).

## Client beacon

`POST /api/analytics/track` — `AllowAnonymous`, JSON `{ event: "..." }`.
Rejects unknown event names (400). Sets the `qs_visitor` cookie when absent.

```js
navigator.sendBeacon('/api/analytics/track',
    new Blob([JSON.stringify({ event: 'visitor' })], { type: 'application/json' }));
```

## Founder dashboard

- **Page**: `/admin/analytics` (`Components/Pages/Admin/Analytics.razor`) —
  initial SSR data on direct GET and Unpoly-targeted GET, admin-gated via `AdminGate`
  (email in `Admin:Emails`); no HX follow-up request.
- **API**: `GET /api/admin/analytics` (admin-only) → `AnalyticsDashboardResponse`:
  - `DailyActiveUsers` — distinct visitor ids per day (default 30-day window)
  - `Funnel` — visitor → upload_started → upload_completed → download_completed
  - `TopFiles` — top 10 by download_completed
  - `GeographicSpread` — top 10 countries by event count
- Aggregation is in-memory over a projected column set (founder-scale volume),
  cached 30s via `CacheService` (`analytics:dashboard:{from}:{to}`).

## Manual Test Checklist

- [ ] Anonymous visit to `/` → a `visitor` event row appears in
      `analytics_events` with a `qs_visitor` cookie set in the response.
- [ ] Upload a file anonymously → `upload_started` then `upload_completed`
      rows with the file name/size/content-type.
- [ ] Download a share link (no OTP) → `download_completed` row within ~2s.
- [ ] Set an OTP on upload; verify wrong code (400) then right code (200) →
      `otp_fail` then `otp_pass` rows.
- [ ] `POST /api/shares/p2p` → `p2p_session_created`; connect a receiver WS to
      `/ws/connect/{code}` and register → `p2p_receiver_joined`.
- [ ] `/d/{code}` for a free-plan sender → `ad_interstitial_viewed` row.
- [ ] New OAuth signup → `signup` row; existing-user login → **no** new row.
- [ ] `/admin/analytics` as admin → DAU/funnel/top-files/geo render; as
      non-admin → 403; anonymous → redirect to `/login`.
- [ ] `Analytics:Enabled=false` → no rows written for any action.
