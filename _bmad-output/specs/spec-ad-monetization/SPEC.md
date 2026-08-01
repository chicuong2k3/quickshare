---
id: SPEC-ad-monetization
companions:
  - ad-component-architecture.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# QuickShareCloud Ad Monetization

## Why

QuickShareCloud operates a freemium model where Starter (free) plan users access the service without paying. To generate revenue from this tier while preserving a premium user experience, the service integrates Google AdMob rewarded video ads. Users voluntarily watch a short video ad in exchange for extra daily bandwidth. This format yields the highest eCPM ($5--$15 VND) among legally compliant ad types in Vietnam, respects user choice (opt-in only, no forced ads), and indirectly encourages upgrades to Pro/Studio. Gambling and 18+ ads are prohibited; Google AdMob's content policies automatically filter restricted categories.

**Anti-bypass is a first-class design requirement.** Users may attempt to bypass ads via ad blockers (uBlock/AdBlock), CSS/HTML/DOM manipulation, JS callback spoofing, or token replay. Every reward path is gated by server-side Google AdMob Server-Side Verification (SSV) — the server independently confirms with Google that a legitimate ad view occurred before granting any quota. No client-side signal is trusted.

## Capabilities

- **CAP-1**
  - **intent:** A free-tier user sees a "Watch ad for +100MB" prompt rendered by `AdComponent.razor`. Clicking it loads and plays a Google AdMob rewarded video ad (15–30s). On completion, the server validates the reward and the UI updates with the new quota.
  - **success:** AdMob JS SDK loads without error. Rewarded ad plays to completion. Server callback `POST /api/ads/reward-callback` receives and verifies the AdMob reward token. UI displays updated bandwidth quota. Ad failure (user closes early, network error) does NOT grant reward and shows a graceful message.

- **CAP-2**
  - **intent:** The server tracks how many rewarded ads a user has completed today. Before rendering the ad prompt, it checks the count. After a verified callback, it increments the count. The count resets at midnight UTC.
  - **success:** User with 0 ads today sees the prompt. User with 3 ads today sees "You've reached today's ad limit — Upgrade to Pro for unlimited." instead. After server restart, count persisted in DB and restored correctly.

- **CAP-3**
  - **intent:** `POST /api/ads/reward-callback` receives an AdMob reward verification token, validates it server-side, checks daily ad count < 3, increments the current day's bandwidth quota by +100MB, and returns updated quota stats. Idempotent — sending the same token twice does not double-count.
  - **success:** First callback with valid token → quota +100MB, daily ad count incremented. Duplicate callback with same token → no quota change, same response. Invalid/missing token → 400. User exceeded daily limit → 429.

- **CAP-4**
  - **intent:** `AdComponent.razor` receives the current user's plan and renders nothing (empty fragment) for Pro/Studio users. For free-tier users, it renders a reward prompt skeleton that lazy-loads the AdMob JS and ad unit via HTMX.
  - **success:** Pro user visits page containing `AdComponent` → zero HTML emitted, zero ad scripts loaded, zero network requests. Free user visits same page → sees reward prompt card rendered. Verified via browser DevTools network tab.

- **CAP-5**
  - **intent:** Every reward grant is independently verified with Google AdMob Server-Side Verification (SSV). The server receives the reward token from the client, calls Google's SSV API to confirm the token corresponds to a legitimate completed ad view. Only Google-confirmed tokens result in quota grants. Invalid, expired, or forged tokens are rejected. Replayed tokens (same hash) are rejected via idempotency check.
  - **success:** Send a valid AdMob token from a real ad completion → Google SSV confirms → quota granted. Send a fake token string → Google SSV rejects → server returns 400. Replay a previously-used valid token → server detects duplicate token hash → returns 200 with no quota change. Token verification timeout (Google SSV unreachable) → server returns 502, no quota granted. Log every verification attempt with outcome for audit.

## Constraints

- Google AdMob is the **only** ad network. Rewarded video is the **only** ad format — no banners, interstitials, pop-ups, or push notifications.
- AdMob SDK loaded via `<script>` tag from Google CDN (`https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js`). No npm package.
- **Google AdMob Server-Side Verification (SSV) is mandatory.** Every reward callback `POST /api/ads/reward-callback` MUST call Google's SSV API to independently verify the reward token. Client-side claim of ad completion is never trusted. No quota is granted without Google confirmation. This is the single defense against all bypass vectors: ad blockers, CSS/HTML/DOM manipulation, JS callback spoofing, and token forgery.
- **Idempotency via token hashing.** Each processed AdMob token's SHA-256 hash is stored in `daily_ad_tracking.token_hashes[]`. Before granting reward, server checks hash doesn't exist. Replayed tokens return 200 with no quota change.
- **Audit logging.** Every reward callback attempt (success, invalid token, duplicate, daily limit exceeded, SSV timeout) is logged with `UserId`, token hash, outcome, and timestamp for abuse pattern detection.
- Maximum **3 rewarded ads per user per day**. Hard server-side enforcement with 429 response when exceeded. Client shows remaining count before displaying prompt.
- Reward amount: **+100MB daily bandwidth** per completed ad. Constant — not configurable per tier (only Starter users see ads).
- `AdComponent.razor` **must** follow the HTMX lazy-load pattern per `AGENTS.md`: shell renders instantly (no DB calls), AdMob JS loads via `hx-get` + `hx-trigger="load"`.
- Pro and Studio users: `AdComponent` renders **zero HTML** (empty Razor fragment). No ad script, no tracking pixels, no network requests for paid users.
- Tech stack: .NET 10, Blazor Static SSR, Htmxor, EF Core, PostgreSQL. JS interop via vanilla JS modules in `wwwroot/js/`. No Blazor WASM.
- All ad-related CSS uses the global `wwwroot/css/app.css` with `ad-` prefix namespace (scoped `.razor.css` does not apply to HTMX responses from a different component).

## Non-goals

- No banner ads, interstitials, pop-ups, pop-unders, or push notification ads.
- No affiliate marketing links within the ad component placement.
- No ad bypass detection on the client side. Ad blockers may prevent the AdMob SDK from loading — this is acceptable; the user simply doesn't see the reward prompt. The server-side SSV gate ensures no reward is granted without Google-confirmed ad completion regardless of client state.
- No ad targeting based on user file content, file names, or share metadata.
- No gambling, adult, political, or alcohol ads — Google AdMob content policies handle filtering.
- No A/B testing of ad formats for initial release. Single rewarded video implementation.
- No server-side ad inventory management or custom ad server. AdMob handles ad selection, bidding, and delivery entirely.

## Success signal

A Starter-plan user with 0 ads today visits a page with `AdComponent` → sees "Watch a short ad to get +100MB extra bandwidth" card → clicks "Watch Ad" → AdMob rewarded video plays (15--30s) → user completes the ad → `POST /api/ads/reward-callback` receives token → server calls Google SSV API → Google confirms valid ad view → server increments daily bandwidth by +100MB → UI updates showing "+100MB added! You have 2 ads remaining today." → user proceeds with their download.

**Anti-bypass verification:** A user spoofs `POST /api/ads/reward-callback` with a fake token → server calls Google SSV → Google rejects → server returns 400, no quota granted, logged as `RewardRejected Reason=InvalidToken`. A user replays a previously-used valid token → server detects duplicate hash → returns 200 with no quota change, logged as `RewardRejected Reason=DuplicateToken`. AdMob SDK blocked by ad blocker → user never sees reward prompt — no reward path exists without Google SSV confirmation.

A Pro user on the same page sees no ad component at all. The server log shows: `RewardGranted UserId=xxx TokenHash=abc123 DayCount=1 BandwidthNow=200MB`.
