# Design: Capture Sender Email on Anonymous Upload (issue #76)

**Date:** 2026-08-15
**Status:** Approved (scope: full issue + verify token)
**Unblocks:** #29 (expiry reminder), #33 (sender download tracking), #34 (recipient file request), #62 (weekly digest)

## Problem

Anonymous senders have zero post-upload channel back to them. WeTransfer/FileMail capture sender email at upload as the core flow. One nullable field on `Delivery` unlocks 4 downstream features.

## Decisions (from brainstorming)

- **Scope:** full issue — field + UI + link email + consent/unsubscribe + rate-limit + verify token.
- **Verify token:** full verification flow (pattern #60): single-use DataProtection token with expiry, sent inside the link email ("Confirm my email"). Clicking marks the delivery's email confirmed.
- **Resend cooldown:** **deferred.** No resend UI exists on `/send`, so no cooldown endpoint is built. Token expiry (24h) is the only guard. A resend flow can be added when a UI exists.

## Data Model

### `UploadSession` (src/QuickShare/Data/Entities/UploadSession.cs)

Add:
- `SenderEmail` (string?, nullable) — entered at StartR2, before upload begins.

### `Delivery` (src/QuickShare/Data/Entities/Delivery.cs)

Add:
- `SenderEmail` (string?, nullable) — copied from session at CommitR2.
- `SenderEmailConfirmed` (bool, default false) — set by confirm-link click.
- `SenderEmailConsent` (bool, default false) — opt-in via checkbox; unsubscribe → false.

New EF migration (pattern: `20260808090221_AllowAnonymousUploads`). Columns: `text` nullable for SenderEmail, `boolean` not-null default false for the two flags.

## API Surface

### StartR2 (modified)

`StartR2Request` + `StartR2Command` gain `SenderEmail` (string?). Validator: if present, `.EmailAddress()` + max length (320). Stored on `UploadSession.SenderEmail`. Email is captured here because the user enters it in the idle section *before* upload starts.

### CommitR2 (modified)

`CommitR2Handler`: copy `session.SenderEmail` → `delivery.SenderEmail`. **Never** from the commit request (matches existing rule: "Metadata comes from the validated session, not the commit request"). Set `SenderEmailConsent = !string.IsNullOrWhiteSpace(session.SenderEmail)`.

After `SaveChangesAsync`, if `delivery.SenderEmail != null && emailSender.IsConfigured` → send link email (see below). `try/catch` + log warning — email failure must NOT fail the commit (pattern: `DownloadNotificationJob`).

### Confirm endpoint (new)

`GET /confirm-sender-email/{token}` — AllowAnonymous.
- Token: DataProtection-signed (`SenderEmailTokens` class, purpose `quickshare.sender-email.confirm`), payload = `deliveryId|expiryUtcTicks`.
- Valid + unexpired + delivery has SenderEmail → set `SenderEmailConfirmed = true`, save, render HTML confirmation page (Vietnamese, pattern: `UnsubscribeEndpoint`).
- Invalid/expired/already-confirmed → 400 HTML page. Already-confirmed check makes the token effectively single-use.

### Unsubscribe endpoint (new)

`GET /unsubscribe-sender/{token}` — AllowAnonymous.
- Token: DataProtection-signed (same `SenderEmailTokens` class, purpose `quickshare.sender-email.unsubscribe`), payload = `deliveryId|expiryUtcTicks`.
- Valid + unexpired → set `SenderEmailConsent = false`, save, render HTML done page.
- Kept separate from existing `/unsubscribe` (that one targets `User.EmailOnDownload` for digest emails).

### New routes (RoutePaths.cs)

- `ConfirmSenderEmail = "/confirm-sender-email"`
- `UnsubscribeSender = "/unsubscribe-sender"`

## Email (link email)

Sent after commit success. Subject + body in Vietnamese (matches existing `DownloadNotificationJob`).

Body contains:
- Share link: `{BaseUrl}/d/{shareCode}`
- Confirm link: `{BaseUrl}/confirm-sender-email/{token}`
- Unsubscribe link: `{BaseUrl}/unsubscribe-sender/{token}`
- Consent footer: "You're receiving this because you chose to be emailed at upload. Unsubscribe anytime."

`SenderEmailTokens` (new class in `Infrastructure/Email/`): mirrors `UnsubscribeTokens` — `Create(deliveryId, expiry)` / `TryRead(token, out deliveryId, out expiry)`, two DataProtector instances with distinct purposes. Default expiry: 24h.

## Rate-Limit Email Capture

Inside `CommitR2Handler` (before sending email), a `ConcurrentDictionary` keyed by `ip:email` (and `ip` alone):
- 5 deliveries per email per hour.
- 20 captures per IP per hour.

Over limit → skip email send (log), **upload still succeeds** — email is an accessory, not a gate. Pattern follows `RateLimitingMiddleware`'s in-memory window approach. No new middleware needed; inline in handler keeps blast radius small. (Ties #52.)

## UI (Upload.razor — anonymous only)

- Idle section: single email input labeled "Email me the link + updates" — entering an email IS the consent (no separate checkbox; matches WeTransfer-style opt-in). Only rendered when `!isAuthenticated` (signed-in users already have email on their account).
- `upload.js`: read email + checkbox; include `senderEmail` in StartR2 POST body; clear on reset.
- i18n: add keys to `SharedLoc.en.resx` / `SharedLoc.vi.resx` (label, placeholder, hint, consent text).
- CSS: scoped `Upload.razor.css` (page is Blazor SSR + JS, not an HTMX partial — scoped is fine).

## Testing

- **Server tests** (QuickShare.Server.Tests):
  - StartR2 with SenderEmail → session persisted with email.
  - CommitR2 → Delivery.SenderEmail + SenderEmailConsent=true; invalid email → 400.
  - Confirm endpoint: valid token → confirmed=true; expired → 400; already-confirmed → 400 (single-use).
  - Unsubscribe endpoint: valid → consent=false; invalid token → 400.
  - Rate limit: 6th delivery to same email in an hour → email skipped, delivery still created.
- **Browser tests**: `UploadDownloadFlowTests` — anonymous flow still passes with optional email omitted.

## Out of Scope (deferred)

- Resend cooldown / resend UI (no UI exists).
- #29/#33/#34/#62 consumers — this issue only captures + stores + confirms the email.
- #59 GDPR export — consent flag stored now, export later.
- Email verification for registered accounts (#60) — separate issue; this reuses its pattern only.
