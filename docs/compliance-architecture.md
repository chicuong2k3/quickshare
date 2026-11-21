# QuickShare — Compliance Architecture

What is left of compliance after the de-bloat. Everything that used to live here — legal hold
(#266), the retention-policy clamp (#161), and the delivery scope rules (#142/#166) — has been
removed; `docs/retired-features.md` records why, and `DropRetiredFeatureSurface` expired the
deliveries that depended on them. Do not reintroduce a control here without a customer who has
asked for it by name.

## What ships

| Area | Code | Duty |
|---|---|---|
| PDPD consent | `Features/Users/Consent/PdpdConsent.cs` | record, show and revoke marketing/analytics consent |
| Right to erasure | `Features/Users/AccountDeletion/AccountDeletion.cs` | delete an account and its deliveries on request |
| Security timeline | `Data/Entities/SecurityEvent.cs`, `SecurityEventService` | an auditable record of sign-in, key, workspace and delivery events the user can see |
| DPA / trust page | `DpaTerms`, `/soc2` page | published terms with a version that keeps meaning what it meant |

## Invariants that still bind

- **Consent is recorded, never inferred.** A row in the consent table with a timestamp and the
  policy version it agreed to is the only evidence that counts; an accepted-flag on the user is
  not consent, it is a memory of one.
- **A consent revoke must take effect on the next request**, not on the next deploy. Any cache
  in front of a consent read is a bug: the person clicking revoke is watching.
- **Erasure deletes what the product stores and says so.** Soft-deleted deliveries are purged,
  object bytes are deleted by key, and the response reports counts — a deletion that reports
  success while buckets still hold the bytes is the failure this path exists to prevent.
- **Never edit published DPA terms or a `/soc2` control status in place.** Bump
  `DpaTerms.CurrentVersion`; on the trust page, "partial" is the honest status for a control
  with no independent evidence behind it.

## What a sender still controls per delivery

Expiry (capped by plan), a download limit, burn-after-download, an optional password (BCrypt,
with lockout), an optional emailed code that proves the opener owns the mailbox, privacy mode
(no counts, masked filename) and comments. Those are the recipient gates, documented in
`docs/send-architecture.md` and enforced in `DownloadDeliveryHandler` plus every preview,
bundle, card and offline path.
