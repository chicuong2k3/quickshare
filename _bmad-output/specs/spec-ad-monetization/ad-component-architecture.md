# AdComponent Architecture

## Overview

`AdComponent.razor` is a reusable Blazor SSR shared component that encapsulates all ad logic: plan gating, AdMob JS loading, rewarded video playback, server callback, and quota display. It is placed in `Components/Shared/` and consumed by any page that needs ad placements for free-tier users.

## File Layout

```
src/QuickShare/
├── Components/
│   └── Shared/
│       └── AdComponent.razor          # Blazor SSR component (shell + HTMX body)
├── Features/
│   └── Ads/
│       ├── RewardCallback/
│       │   ├── RewardCallbackEndpoint.cs   # POST /api/ads/reward-callback
│       │   └── RewardCallbackHandler.cs    # Verify token, grant quota
│       ├── DailyAdCount/
│       │   └── GetDailyAdCountHandler.cs   # Query: ads completed today
│       └── Shared/
│           └── AdConstants.cs              # Limits, reward amounts, IDs
├── wwwroot/
│   └── js/
│       └── ad-component.js            # AdMob init, reward lifecycle, callback dispatch
└── wwwroot/
    └── css/
        └── app.css                     # Global styles: .ad-reward-card, .ad-reward-btn, etc.
```

## Component Design

### AdComponent.razor

```razor
@* AdComponent.razor — reusable rewarded ad component *@
@* Renders nothing for Pro/Studio. For free users: HTMX lazy-loaded reward prompt. *@

@if (UserPlan != "free")
{
    @* Pro/Studio: zero output — no HTML, no scripts, no network requests *@
    return;
}

@* Free-tier reward prompt skeleton *@
<div id="ad-reward-card"
     class="ad-reward-card"
     hx-get="/ads/reward-prompt?remaining=@RemainingAds"
     hx-trigger="load"
     hx-target="#ad-reward-card"
     hx-select="#ad-reward-card"
     hx-swap="outerHTML">
    <div class="ad-reward-card__skeleton">
        <div class="db-skeleton db-skeleton--text" style="width:60%"></div>
        <div class="db-skeleton db-skeleton--button" style="width:40%"></div>
    </div>
</div>
```

```csharp
// AdComponent.razor.cs — code-behind
[Parameter] public string UserPlan { get; set; } = string.Empty;
[Parameter] public int RemainingAds { get; set; } = AdConstants.MaxDailyAds;

protected override void OnInitialized()
{
    var hxHeader = HttpContextAccessor.HttpContext?.Request.Headers["HX-Request"].FirstOrDefault();
    if (hxHeader != "true") return; // shell render — show skeleton

    // HTMX request: load AdMob JS + wire up reward button
    // Actual JS init handled client-side via ad-component.js
}
```

### Parameters

| Parameter | Type | Source | Description |
|-----------|------|--------|-------------|
| `UserPlan` | `string` | Injected via `IHttpContextAccessor` → `GetUserAsync()` | Controls plan-gated rendering. Pro/Studio → empty output. |
| `RemainingAds` | `int` | Computed from `GetDailyAdCountHandler` or passed by parent | Remaining ad count for today (max 3 minus completed). Shown in prompt text. |

### CSS Classes (global `app.css`)

```css
/* Ad reward card — global styles (scoped .razor.css won't apply to HTMX responses) */
.ad-reward-card {
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    padding: var(--space-4);
    background: var(--color-background);
    margin-top: var(--space-4);
}

.ad-reward-card__skeleton {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
}

.ad-reward-card__body {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
    align-items: center;
    text-align: center;
}

.ad-reward-card__title {
    font-weight: 600;
    color: var(--color-foreground);
}

.ad-reward-card__description {
    color: var(--color-muted);
    font-size: 0.875rem;
}

.ad-reward-btn {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-4);
    border-radius: var(--radius-md);
    background: var(--color-primary);
    color: white;
    border: none;
    cursor: pointer;
    font-weight: 500;
    transition: opacity 0.2s;
}

.ad-reward-btn:hover { opacity: 0.9; }
.ad-reward-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.ad-upgrade-link {
    color: var(--color-accent);
    font-size: 0.875rem;
    text-decoration: underline;
    cursor: pointer;
}
```

---

## JS Module: ad-component.js

### Interface

```js
// ad-component.js — Google AdMob rewarded video lifecycle

/**
 * Initialize AdMob for the reward card element.
 * Called by HTMX after the ad prompt HTML is swapped in.
 * @param {Object} config
 * @param {string} config.adUnitId - AdMob ad unit ID
 * @param {string} config.callbackUrl - POST /api/ads/reward-callback
 * @param {string} config.antiForgeryToken - Anti-forgery token for POST
 * @param {number} config.remainingAds - Ads left today
 */
window.QuickShareAds = {
    init(config) {
        // 1. Load AdMob SDK if not loaded
        // 2. Create rewarded ad instance
        // 3. Wire "Watch Ad" button click → ad.show()
        // 4. On reward granted → POST callbackUrl with { token }
        // 5. On success → update card HTML with new quota
        // 6. On failure/close → show "Ad not completed" message
    },

    /** Show the rewarded ad. Returns promise that resolves on completion or rejects on skip/error. */
    showRewardedAd(adUnitId) { /* ... */ },

    /** Send reward verification to server. */
    async verifyReward(callbackUrl, token, antiForgeryToken) { /* ... */ },

    /** Destroy ad instance and clean up. */
    destroy() { /* ... */ }
};
```

### Flow

```
┌─────────────────────────────────────────────────────────┐
│  AdComponent.razor lifecycle (HTMX lazy-load)            │
│                                                          │
│  1. Page renders shell:                                  │
│     <div id="ad-reward-card" hx-get="..." hx-trigger=load>│
│     → skeleton visible instantly                         │
│                                                          │
│  2. HTMX fires GET /ads/reward-prompt                    │
│     → server checks User.Plan, daily count               │
│     → returns HTML with: title, description, button,     │
│       <script>QuickShareAds.init({...})</script>         │
│                                                          │
│  3. ad-component.js init():                              │
│     → loads adsbygoogle.js if needed                     │
│     → creates RewardedAd instance                        │
│     → wires button.onclick = showRewardedAd()            │
│                                                          │
│  4. User clicks "Watch Ad":                              │
│     → showRewardedAd() → ad.show()                       │
│     → Google AdMob plays rewarded video                  │
│                                                          │
│  5a. Ad completed:                                       │
│     → reward callback fires with token                   │
│     → verifyReward() → POST /api/ads/reward-callback     │
│     → server validates, returns { success, quotaNow }    │
│     → card updates: "+100MB added! (2 ads left today)"   │
│                                                          │
│  5b. Ad skipped/error:                                   │
│     → card shows: "Ad not completed. Try again?"         │
│     → button re-enabled                                  │
└─────────────────────────────────────────────────────────┘
```

---

## Server Endpoint: POST /api/ads/reward-callback

### Request

```json
POST /api/ads/reward-callback
Content-Type: application/json
RequestVerificationToken: <anti-forgery>

{
  "token": "AdMob-reward-verification-token-string"
}
```

### Response

```json
// 200 OK — reward granted
{
  "success": true,
  "quotaAddedMb": 100,
  "quotaNowMb": 300,
  "adsRemainingToday": 2
}

// 400 Bad Request — invalid/missing token
{
  "success": false,
  "error": "Invalid reward token"
}

// 429 Too Many Requests — daily limit exceeded
{
  "success": false,
  "error": "Daily ad limit reached (3/3)"
}
```

### Handler Logic

```
RewardCallbackHandler:
  1. Resolve user from auth cookie → GetUserAsync()
  2. If User.Plan != "free" → 403 (Pro/Studio should never reach this)
  3. Fetch today's ad count from DB
  4. If count >= MaxDailyAds (3) → 429
  5. Validate AdMob reward token server-side
     (AdMob Server-Side Verification API or SDK)
  6. If token invalid → 400
  7. Check idempotency: token already used today? → 200 (same response, no double grant)
  8. Increment daily ad count
  9. Increment UserUsage.TotalBytesUploaded (or a separate DailyBandwidthBonus field)
     by +100MB for the current day
  10. Save token hash to prevent replay
  11. Return 200 with updated stats
```

### AdMob Server-Side Verification

Google provides a [Server-Side Verification (SSV)](https://developers.google.com/admob/android/rewarded-video-ssv) API. The flow:

1. Client receives `rewardItem` with a verification token after ad completion
2. Client sends token to our server (`POST /api/ads/reward-callback`)
3. Server calls Google's SSV endpoint to verify the token is legitimate
4. Only if Google confirms → grant quota

This prevents client-side reward forgery.

---

---

## Anti-Bypass Architecture

### Threat Model

| Vector | Attack | Defense |
|--------|--------|---------|
| **Ad blocker** (uBlock Origin, AdBlock Plus) | Blocks `adsbygoogle.js` from loading | AdMob SDK never loads → no reward token generated → server has nothing to verify. User simply doesn't see reward prompt. No quota granted without Google SSV. |
| **CSS manipulation** | `display: none` on `.ad-reward-card`, user clicks hidden element | Irrelevant — Google only issues reward token after real video completion. CSS tricks don't produce valid tokens. |
| **HTML DOM removal** | Delete ad `<div>`, inject fake button | Irrelevant — no Google SDK instance, no token generation. Server SSV rejects anything not from Google. |
| **JS callback spoofing** | `fetch('/api/ads/reward-callback', {body: JSON.stringify({token: "fake"})})` | 🔴 **Primary threat.** Defense: server calls Google SSV API with token. Google returns `invalid` for fake tokens → server returns 400. |
| **JS interceptor / monkey-patch** | Override `QuickShareAds.verifyReward()` to skip ad, send stolen token | Stolen token is from another user's ad → Google SSV binds tokens to ad unit + app. Cross-app tokens rejected. Same-app replay caught by `token_hashes[]` idempotency. |
| **Token replay** | Capture valid token via network tab, resend it | `daily_ad_tracking.token_hashes[]` stores SHA-256 of every processed token. Duplicate hash → 200 with no quota change. |
| **Automation (Puppeteer/Playwright)** | Script auto-plays ad | Google AdMob has bot detection. Automated traffic gets no reward token or gets it invalidated. Google's anti-fraud systems handle this. |
| **Fake Google SSV response** | MITM proxy that fakes `200 OK` from Google SSV | Server calls Google SSV over HTTPS with certificate validation. MITM requires compromising server's TLS trust store. |
| **DevTools inspection** (F12, Ctrl+Shift+I, Inspect Element) | User opens DevTools to inspect ad component, read source, capture network tokens, or modify DOM/CSS | 🟡 **Supplemental defense.** `anti-devtools.js` detects `debugger` pause timing (>100ms gap) → `location.reload()` silently refreshes page. Production only; bypassed by `?debug=true`. Primary defense remains Google SSV + token hash idempotency.

### Defense-in-Depth: Server-Side Verification Flow

```
┌──────────┐     ┌──────────────┐     ┌─────────────┐
│  Client  │     │  QuickShare  │     │  Google SSV │
│ (Browser)│     │  Server      │     │  API        │
└────┬─────┘     └──────┬───────┘     └──────┬──────┘
     │                   │                    │
     │  Ad completed     │                    │
     │  → reward token   │                    │
     │                   │                    │
     │  POST /api/ads/   │                    │
     │  reward-callback  │                    │
     │  { token }        │                    │
     │──────────────────▶│                    │
     │                   │                    │
     │                   │  1. SHA-256(token) │
     │                   │  2. Check token_hashes[] │
     │                   │     → if found: 200 no-op│
     │                   │                    │
     │                   │  3. Call Google SSV│
     │                   │  GET /ssv?token=X  │
     │                   │───────────────────▶│
     │                   │                    │
     │                   │  ◀── { valid:      │
     │                   │     true/false,    │
     │                   │     reward_amount, │
     │                   │     timestamp }    │
     │                   │                    │
     │                   │  4. If invalid →   │
     │                   │     400 + log      │
     │                   │  5. If valid →     │
     │                   │     grant +100MB   │
     │                   │     store hash     │
     │                   │     log success    │
     │                   │                    │
     │  ◀── 200 {         │                    │
     │   success: true,   │                    │
     │   quotaAddedMb:100 │                    │
     │  }                 │                    │
     │                    │                    │
```

### SSV Timeout & Fallback

- Google SSV API timeout: **5 seconds**. If Google is unreachable, server returns **502 Bad Gateway** — no quota granted.
- Rationale: Never grant quota on uncertainty. A timeout means we cannot verify the ad. User must retry.
- SSV call is synchronous within the request handler. Wolverine outbox NOT used for this — reward grant is instantaneous, not eventual-consistency.

### Audit Log Schema

```sql
-- Structured log entries (via ILogger, shipped to structured log sink)
-- Pattern: "Reward{Outcome} UserId={userId} TokenHash={hash} DayCount={n} Reason={reason}"

-- Examples:
-- RewardGranted UserId=abc TokenHash=sha256xyz DayCount=1 BandwidthNow=200MB
-- RewardRejected UserId=abc TokenHash=sha256fake DayCount=0 Reason=InvalidToken
-- RewardRejected UserId=abc TokenHash=sha256dup DayCount=2 Reason=DuplicateToken
-- RewardRejected UserId=abc TokenHash=sha256new DayCount=3 Reason=DailyLimitExceeded
-- RewardRejected UserId=abc TokenHash=sha256new DayCount=0 Reason=SSVTimeout
```

### Abuse Detection Heuristics (future phase, not v1)

| Pattern | Signal | Action |
|---------|--------|--------|
| High invalid-token rate from same UserId | Brute-force token guessing | Rate-limit callback endpoint per user (e.g., 10 req/min) |
| Exact timing intervals between callbacks | Automation script | Flag for review |
| Multiple users sharing same IP with identical token patterns | Token farm | Flag for review |

> These heuristics are **not implemented in v1** — they are noted here so the logging schema supports them when needed. v1 relies entirely on Google SSV + idempotency + daily limit.

### DevTools Detection & Auto-Reload (supplemental defense)

**Status: Production only. Configurable. Has escape hatch.**

When a user opens DevTools (F12, Ctrl+Shift+I, right-click → Inspect), the `debugger` statement pauses JavaScript execution. By measuring execution timing around a `debugger` call in a loop, we detect the pause and trigger `location.reload()`.

#### Implementation

```js
// wwwroot/js/anti-devtools.js
// Only injected in production. Skipped when ?debug=true URL param is present.
(function() {
    'use strict';

    // Escape hatch: devs can add ?debug=true to bypass detection for current session
    if (window.location.search.includes('debug=true')) return;

    function detectDevTools() {
        var before = performance.now();
        debugger;                          // DevTools open → pause here → timing gap
        var after = performance.now();
        return (after - before) > 100;     // >100ms gap = DevTools detected
    }

    // Check periodically. 2000ms interval — <0.0001% CPU. DevTools takes seconds to open; 2s detection is more than enough.
    setInterval(function() {
        if (detectDevTools()) {
            location.reload();             // Immediate refresh — wipes any inspection state
        }
    }, 2000);
})();
```

#### Production-Only Injection

```razor
@* In App.razor or MainLayout.razor *@
@using Microsoft.AspNetCore.Hosting
@inject IWebHostEnvironment Env

@if (Env.IsProduction())
{
    <script src="~/js/anti-devtools.js" asp-append-version="true"></script>
}
```

#### Threat Model Update

| Vector | Before (no DevTools detection) | After (with DevTools detection) |
|--------|------|------|
| **User opens DevTools to inspect ad component** | Can view HTML, CSS, network calls, JS source | Page immediately refreshes — inspection window closes |
| **User opens DevTools to find/copy ad reward token** | Can see token in Network tab → replay attempt | Page refreshes before they can capture token → must replay blind (caught by SSV + hash) |
| **User opens DevTools to modify DOM/CSS** | Can experiment with hiding/restyling ad | Page refreshes → DOM reset |
| **User has "Deactivate breakpoints" enabled** | — | Detection fails (no pause on `debugger`) — but SSV still active |
| **User has React/Vue DevTools extension** | — | Extensions that trigger JS evaluation may cause false positive. Mitigated by 100ms threshold. |

#### Limitations (documented, not hidden)

1. **Bypassable:** User with "Deactivate breakpoints" enabled in DevTools settings defeats detection entirely
2. **Not primary defense:** DevTools detection is a speed bump — Google SSV remains the actual security boundary
3. **False positives possible:** Some browser extensions may trigger the timing gap. The 100ms threshold minimizes this; if it still occurs, user sees an unexplained refresh (acceptable degradation for free tier)
4. **No user-facing message:** Page simply reloads silently. No alert, no warning dialog — that would confirm to the attacker that detection exists

## Data Model Changes

### New Entity: DailyAdTracking

```sql
CREATE TABLE daily_ad_tracking (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id),
    date        DATE NOT NULL DEFAULT CURRENT_DATE,
    ad_count    INT NOT NULL DEFAULT 0,
    token_hashes TEXT[] NOT NULL DEFAULT '{}',  -- SHA-256 of used tokens (idempotency)
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ,
    UNIQUE(user_id, date)
);
```

| Field | Purpose |
|-------|---------|
| `user_id` + `date` | Unique per user per day — single row tracks today's ad count |
| `ad_count` | Number of completed rewarded ads today (0–3) |
| `token_hashes` | SHA-256 hashes of processed tokens — prevents double-counting |

### UserUsage Extension

Add `DailyBandwidthBonus` field (or reuse `TotalBytesUploaded` logic with an offset):

```sql
ALTER TABLE user_usages ADD COLUMN daily_bandwidth_bonus BIGINT NOT NULL DEFAULT 0;
```

`DailyBandwidthBonus` is separate from `TotalBytesUploaded` — it tracks the **extra** quota granted via ads, resetting daily. The effective monthly limit becomes `PlanLimit + DailyBandwidthBonus * days_in_month`.

Simpler alternative: Modify `QuotaEnforcer` to check `daily_bandwidth_bonus` as an additive offset to the free plan's 2GB monthly cap. The bonus resets daily via a background job or on-demand check comparing `PeriodStart` dates.

---

## Integration Points

### Pages that include AdComponent

| Page | Placement | Trigger |
|------|-----------|---------|
| `Download.razor` | After password/OTP gate, before download button | User on free plan trying to download |
| `Upload.razor` | After file exceeds free quota | User on free plan hitting upload limit |
| `Files.razor` | Optional — files listing page | Free user may want extra bandwidth before uploading |

### Plan Constants

From `PlanTierLimits.cs`:

```csharp
public static class AdConstants
{
    public const int MaxDailyAds = 3;
    public const long RewardBandwidthBytes = 104_857_600L; // 100 MB
    public const string AdMobAdUnitId = ""; // From config/secrets
    public const string GoogleAdScriptUrl = "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js";
    public const string AdComponentCssPrefix = "ad-";
}
```

---

## Error States

| Error | Detection | User Sees |
|-------|-----------|-----------|
| AdMob SDK failed to load | JS `onerror` / timeout | "Ads unavailable right now. Try again later." — prompt hidden |
| Ad not completed (user closed) | AdMob `onRewardedAdClosed` without reward | "Ad not completed. Watch the full ad to earn +100MB." — button re-enabled |
| Network error on callback | `fetch()` rejection | "Could not verify reward. Please try again." — retry prompt |
| Server rejects token (invalid/duplicate) | 400 response | "Reward verification failed." — prompt hidden |
| Daily limit reached | 429 response | "You've reached today's ad limit. Upgrade to Pro for unlimited." |
| User is Pro/Studio | Component check | No ad component rendered at all |
