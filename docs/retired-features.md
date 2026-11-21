# Retired features

Every subsystem below was deleted, not hidden behind a flag. The docs that describe them
(`compliance-architecture.md`, `delivery-gating.md`, `protocol-mounts.md`,
`passwordless-auth.md`, `enterprise-identity.md`, …) are history; the code no longer has it.

Why: QuickShare sells one thing — a Vietnamese SMB sends a file, a recipient with no account
opens the link from Zalo. Anything that only made sense to a compliance buyer, a developer
tooling ecosystem, or a browser-only sign-in flow added surface nobody used, and each one was
a second way to do something the product already did.

## How a feature is retired here

A removal is one batch and three parts:

1. Delete the endpoint, handler, entity, UI block, script and error codes.
2. Delete the tests that only covered it, and re-point the tests that used it incidentally
   (seeding a column, a DTO member) at the surviving shape.
3. **Expire the rows that relied on it.** `20261121192101_DropRetiredFeatureSurface` runs an
   `UPDATE` before the column drops so a delivery whose only gate was a retired control ends
   up expired instead of open. A retired protection must never loosen a live share.

## Removed in the 2026-11 de-bloat

| Area | What went | Why it went |
|---|---|---|
| Developer ecosystem | SFTP keys and ingest, GitHub repo linking and release webhook, SDK/self-host manifests, Telegram and Discord bots, desktop/extension/Zalo/iOS manifests | No customer asked; every one was an integration surface to maintain against a moving spec |
| Sending & mail | SMS delivery, custom sending domains, email ingest address, upload presets (`#100`, `/account/presets`, `/api/upload-presets`, `UploadPresets`) | SMS is not how VN shares files; domain verification was a support load with an email-reputation story; upload presets were broken UI and unnecessary form-prefill storing plaintext share passwords (senders set options on `/send` or via workspace folder defaults `#245`) |
| Delivery protection | sender-set 6-digit code (`OtpHash`), IP allowlist (#142), country rules (#166), encryption-mode choice with key escrow | One gate is enough — the password, plus the emailed code where a sender wants proof of ownership. A code the sender types into the same chat as the link protects nothing |
| Storage & scan | virus scan (#58), cold storage tier, HLS preview variants, storage residency (#119), legal hold (#266), retention policy clamp (#161) | Controls for a buyer this product does not have yet; residency in particular was a routing rule nobody had asked to configure |
| Billing | PO and net terms (#192), student/nonprofit discounts, gift subscriptions, recurring subscriptions (#176), pay-per-transfer credits, crypto rail (#225) | Invoice, seats and overage VAT-style billing are what SMBs use; the rest was procurement theatre |
| Workspace | approval workflow (#162), SIEM sinks (#191), scheduled workspace backup (#271), SCIM, encryption policy (#119) | Team features that shipped before any team asked for them |
| Auth | passkeys (#68), magic-link sign-in (#132), passkey recovery codes (#118), TOTP challenge route, MFA session | Accounts come from OAuth or SAML; a second sign-in path is a second thing to secure and revoke |
| Content | OCR and full-text search over images (#243), voice commands, media tools stubs, A/B experiments, affiliate program, migration wizard, automations/Zapier-Make, cloud import, LiveStream progress | Never reached an end user; several were rendered as empty panels |
| Languages | zh, ja, es, ar | vi and en only. A half-translated page is worse than an untranslated one |

## What replaced the removed choice

- **Encryption** is AES-256-GCM with the key in the URL fragment, always. The server never
  holds it, so a database dump is not a file dump. There is no "none" and no passphrase mode to
  pick wrongly, and no escrow for the server to be asked for.
- **Protection** is one optional password (hashed with BCrypt, lockout after repeated failures)
  plus an optional emailed code that proves the opener owns the mailbox. Both gates are
  enforced in `DownloadDeliveryHandler`, and every recipient path — preview, zip listing,
  bundle, card, offline bundle — asks the same question about the same two fields.
- **Expiry** is the sender's choice, capped by plan. No retention ceiling narrows it further.

## Still true after all of this

Comments, chat channels, white-label domains, workspace folders and audit, API keys, object
store configuration, PDPD consent, and the recipient gates all stay, with the invariants
written in `AGENTS.md`.
