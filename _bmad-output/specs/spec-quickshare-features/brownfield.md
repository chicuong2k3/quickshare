# Brownfield reference — QuickShareCloud as-built state

Existing-state reference for the current product (verified against the codebase, not aspirational). Capability IDs in SPEC.md anchor to these anchors.

## Stack

- Blazor Static SSR (.NET 10), HTMX via HTMXor (`Htmxor`), no Direct Routing registered (incompatible with .NET 10 — see SPEC constraint).
- Storage: R2 (chunked/multipart upload path); metadata in PostgreSQL via EF Core.
- Backend pipeline: FastEndpoints (REST) + Wolverine (messaging/handlers). FusionCache for read-heavy data.
- Auth: FastEndpoints cookie auth; Google OAuth configured, GitHub button present.
- Payments: PayOS (VietQR / card / e-wallet), webhook-driven plan changes.
- P2P: WebRTC signaling relay for device-to-device transfers (P2PProtocol constants).
- Client crypto: AES-256-GCM in-browser; key in URL fragment; zero-knowledge server.

## Pages / routes

- Home (upload flow, drag/drop + paste).
- Download page (recipient, no-JS default phase; `DownloadNoScriptTests` guards).
- Login / Google OAuth entry.
- Plan pricing page (`spec-pricing-page` companion artifacts exist: `pricing-tiers.md`).
- Tools landing pages planned: share-text, online-clipboard, uncompressed-images.

## Domain entities (as-built)

`TempShare`, `Manifest`, `SystemSetting`, `AppFeatureFlag`, `AppConfig`, `AppUser`, `PlanTier`, `PerkDefinition`, `PerkOwner`, `Subscription`, `PaymentIntent`. Audit fields via `BaseAuditEntity` + `AuditableEntityInterceptor` (never set manually).

## Limits sources

- `PerkOwnerLimits` — anonymous/perk-based caps (anonymous = 100 MB).
- `PlanTierLimits` — per-plan caps (Free / Pro / Studio).

## Existing spec artifacts (sibling folders under `_bmad-output/specs/`)

- `spec-payment` (PayOS products, payment-events-schema)
- `spec-pricing-page` (pricing tiers)
- `spec-r2-storage` (upload/download flow)
- `spec-webrtc-p2p` (P2P protocol + pricing impact)
- `spec-ad-monetization` (AdMob rewarded ads; note: AdMob contradicted by later decision — see Open Question Q-4)
- `spec-enforce-login`
- `spec-quicksharecloud` (STALE pre-merge artifact — not a valid canonical reference)

> The sibling specs were authored at different times and some carry contradictions (e.g. ad provider). Where a sibling spec conflicts with this SPEC, this SPEC wins; unresolved conflicts are listed in Open Questions.
