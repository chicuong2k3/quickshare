---
id: SPEC-quickshare-features
companions:
  - brownfield.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# QuickShareCloud — Current product feature set

## Why

QuickShareCloud is the fastest way to share temporary files online: drag a file, get a link, the recipient downloads it — no accounts on either side. It is a Blazor Static SSR monolith (server-rendered, HTMX lazy-load) backed by R2 storage and PostgreSQL, with client-side AES-256-GCM encryption (zero-knowledge), plan-based quotas, PayOS payments, and optional device-to-device WebRTC transfers. This spec is the canonical contract for the *existing* feature inventory — each capability is one user story, so downstream work (stories, tasks, tests) traces back to a single source of truth.

## Capabilities

- **CAP-1**
  - **intent:** Sender uploads a file (drag/drop, paste, or picker) and receives a share link without signing up.
  - **success:** File lands on R2 storage via the chunked/multipart path and a share link with an unguessable token is returned; anonymous senders capped at 100 MB, signed-in senders capped by plan; works from mobile and desktop browsers.

- **CAP-2**
  - **intent:** Every delivery is AES-256-GCM encrypted in the browser; the key rides only in the URL fragment so the server stores pure ciphertext.
  - **success:** Server and DB never see plaintext or key material; a recipient download in a fresh browser reconstructs a byte-identical file from the fragment key; corrupted ciphertext fails closed.

- **CAP-3**
  - **intent:** Recipient opens the share link on any device and downloads the file without creating an account.
  - **success:** A fresh browser downloads the file in one click; no login wall; works on mobile and desktop.

- **CAP-4**
  - **intent:** Sender can protect a delivery with a 6-digit OTP, a password, or both; free-plan recipients may also face a short ad interstitial before download.
  - **success:** Unverified attempts are blocked; correct OTP/password passes; ad interstitial appears at most once per free-plan delivery and the reward is recorded; gates are sender-controlled per delivery.

- **CAP-5**
  - **intent:** Deliveries carry a server-enforced expiry fixed per plan (Free/Anonymous 24 h, Pro 30 d, Studio 90 d — no user choice); expired links show a clear expired state and background jobs delete the data.
  - **success:** An expired link renders an expiration message, never a broken page; cleanup jobs remove expired objects from storage and DB; expiry is enforced server-side, not client-side.

- **CAP-6**
  - **intent:** Free/Pro/Studio tiers cap per-file size, monthly transfer volume, and delivery count; quota state is visible and enforced server-side.
  - **success:** Over-quota uploads are blocked with a clear message; limits come from a single plan-limits source and are enforced on the server; an upgrade CTA surfaces at the limit.

- **CAP-7**
  - **intent:** Sender and recipient transfer files directly device-to-device via a share code and WebRTC signaling; file bytes never touch the server.
  - **success:** Two browsers negotiate through the signaling relay, establish a data channel, and transfer the file directly; the server stores no bytes.

- **CAP-8**
  - **intent:** Sender upgrades via PayOS (VietQR bank transfer, card, e-wallet); the webhook flips the plan and the subscription lifecycle (renew/cancel/expire) holds.
  - **success:** Payment intent → PayOS checkout → webhook → plan upgraded; expiry/cancel downgrades the plan; status and receipts visible in billing.

- **CAP-9**
  - **intent:** Sender signs in with Google (GitHub button present) and manages profile, delivery history, and billing.
  - **success:** User edits name/avatar, sees past deliveries, and sees billing state; FastEndpoints enforces auth by default with `AllowAnonymous` only where intended.

- **CAP-10**
  - **intent:** Sender receives an email when a delivery is downloaded.
  - **success:** A download event triggers an email to the sender with delivery metadata; unsubscribe is honored.

- **CAP-11**
  - **intent:** Share-text, online-clipboard, and uncompressed-images landing pages drive users into the send flow.
  - **success:** Each page renders its static shell instantly (HTMX lazy-load) and routes the CTA into the upload/send flow.

## Constraints

- HTMX lazy-load is the default for every page with `OnInitializedAsync`: static shell renders instantly (`HX-Request` absent → return immediately), dynamic data fetched via `hx-get` + `hx-trigger=load` after load. All internal navigation uses `hx-get` targeting the content area, never plain anchors. No `[StreamRendering]` as primary approach.
- Every `hx-get`/`hx-post` must carry `hx-select` for the target element — HTMXor Direct Routing (`AddHtmxorComponentEndpoints`) is NOT compatible with .NET 10 and is not registered, so partial responses return full HTML documents that must be extracted client-side.
- `MainLayout` must inherit `LayoutComponentBase` (never `HtmxLayoutComponentBase`). `HtmxorLayout` inherits `HtmxLayoutComponentBase` and renders only `@Body` for partials via `[HtmxLayout(typeof(HtmxorLayout))]` in `_Imports.razor`.
- No `hx-boost` globally. Blazor enhanced navigation handles full-page links; HTMX only for genuine partial server updates (search, forms, real-time data). Static data toggles (pricing monthly/yearly, theme) stay client-side — never HTMX.
- Design tokens: `--db-*` names are an override layer on the DAUB vendor stylesheet — change values, never names (~322 `db-*` usages depend on them). Foil gold is a fill only, never text color (use `--db-color-accent-text`). Valley blue = active cell; mountain gray = rest. Radius zeroed; geometry is `skewX(-14deg)` with glyphs counter-skewed. Font sizes only from DESIGN.md `typography.scale` closed set.
- Download page must work with no JavaScript: the server renders a usable phase and JS only moves on from it. `class="hidden"` must never be on the default phase (`DownloadNoScriptTests` guard). Browser-only logic (clipboard, sessionStorage, phase switching) is covered by `BrowserAppFixture` + Playwright.
- E2E encryption: AES-256-GCM per-chunk AEAD; key lives only in the URL fragment; AAD binds the manifest. Every upload is encrypted by default — no plaintext toggle. Server is zero-knowledge: it never sees plaintext or key material.
- FastEndpoints enforces auth by default. Declare `AllowAnonymous()` explicitly for public endpoints (delivery download, webhook, payment callback, brand info). Never gate on `HttpContext.User.Identity?.IsAuthenticated` manually.
- Never set `CreatedAt`/`UpdatedAt` manually — `AuditableEntityInterceptor` sets both from `DateTime.UtcNow`. Entities inherit `BaseAuditEntity` for both; partial entities get metadata-driven support.
- Entity-to-DTO mapping via Mapperly (`Riok.Mapperly`), mappers in `Features/<Feature>/Shared/<Entity>Mapper.cs`. Exceptions: LINQ `.Select()` projections with server-side subqueries, and simple value-object responses.
- FusionCache for all read-heavy paths (`CacheService` via DI for Mediator handlers/endpoints, `StaticCache.Instance` for Razor inline). TTL bands: download info 30 s, file/folder lists 15–30 s, user/billing 2–5 min, plan limits 1 h+, ad tracking 1 min. Every mutation handler must invalidate affected cache keys after `SaveChangesAsync`. Kill switch: `FusionCache.Enabled=false`.
- User-facing strings live in `SharedLoc.{en,vi}.resx` — never hardcode display copy. JS gets strings via a rendered JSON block (`download-i18n` pattern), so `.js` files carry no English literals.
- Isolation CSS per component (`.razor.css`); JS goes in `wwwroot/js/` (no `.razor.js`). Markup returned inside an HTMX partial lacks the component scope attribute, so it must be styled from `wwwroot/css/app.css` with prefixed global classes (`account-`, `files-`, …).
- Anonymous upload cap is 100 MB (`PerkOwnerLimits`). One statement per line in C#. `string.Empty`, never `""` (except where C# mandates compile-time constants). No magic strings/numbers — protocol identifiers, HTTP headers, route paths, config keys, entity statuses, roles live in static class constants (`AuthConstants`, `PlanTierLimits`, `P2PProtocol`, `RoutePaths`).

## Non-goals

- No WebAssembly / InteractiveServer — Blazor Static SSR only.
- No user file library, folder browser, or file/folder management (files/folders CRUD was removed in the QuickShare consolidation). Deliveries are fire-and-forget shares.
- No permanent storage — every delivery expires; nothing is archived server-side indefinitely.
- No file preview, thumbnail generation, or media transcoding on the download page.
- No recipient accounts — recipients never sign up or log in; download is anonymous.
- No copyright enforcement / DMCA compliance system.

## Success signal

A person on any modern browser visits QuickShareCloud, drags a file or pastes an image, and within seconds has an encrypted share link in their clipboard. The recipient opens the link on a phone — no account, one click — and gets a byte-identical file. Expired links show a clean expiration state, never a broken page. A free user whose 100 MB cap or quota is hit sees a clear message and an upgrade path; paying via PayOS flips the plan through the webhook. A pair of browsers can transfer a file directly device-to-device with zero server storage. Every capability above is independently demonstrable in a fresh browser session.

## Assumptions

- Product name is QuickShareCloud; the stale config key `TeleCloud` (project_name/cli config) does not reflect the shipped product.
- Free tier: anonymous cap 100 MB, per-file ≤ 100 MB, ~2 GB monthly transfers, 24 h expiry (`PerkOwnerLimits`). Pro: ≤ 2 GB/file, ~20 GB transfers, 30 d expiry. Studio: ≤ 4 GB/file, ~100 GB transfers, 90 d expiry (`PlanTierLimits`). Expiry is fixed per plan — no user-facing selector.
- Ad interstitial and rewarded ads apply to free-plan users only, gated on plan at render time.

## Resolved questions

- **Q-1 (resolved):** GitHub OAuth is configured in config but NOT set up — the button is present-but-inert. CAP-9 ships with Google-only functional login; GitHub renders but has no wired OAuth client until configured.
- **Q-2 (resolved):** `PerkOwnerLimits` wins for anonymous senders — the 100 MB anonymous cap is the effective ceiling for a new anonymous session, regardless of free-tier `PlanTierLimits` values. Signed-in free users follow `PlanTierLimits`.
- **Q-3 (resolved):** Download notifications are PER-DOWNLOAD — an email fires on each download event (not once per delivery). Volume accepted; unsubscribe + rate-limiting still apply.

## Open Questions

- **Q-4:** Rewarded ad (CAP-4 monetization): exact reward definition (+100 MB daily?) and daily cap (3/day?) — DEFERRED, decide later before implementing the reward path.
