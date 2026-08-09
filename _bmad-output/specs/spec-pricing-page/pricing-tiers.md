# Pricing Tiers (2026-08 Revamp)

> Single source of truth for QuickShareCloud subscription tiers.
> All limits are config-driven via `appsettings.json` → `Pricing:Limits`.
> This document describes the *default* values and rationale.

## Strategic Model

- **Free** = instant delivery. High file size, zero storage. Files auto-delete after short TTL. Monetized via ads.
- **Pro** = real cloud storage for individuals. 30-day retention, password protection, priority support.
- **Studio** = storage + branding for teams/creators. 90-day retention, custom branding, API access.

## Billing

- **Monthly** — billed every month.
- **Yearly** — billed annually, ~17% discount (2 months free).
- All prices in VND (₫). No decimal amounts.

## Tier Details

### Free — ₫0

| Limit | Value | Rationale |
|---|---|---|
| File size | 5 GB | Beats Telegram (2 GB) and WeTransfer free (2 GB) |
| Monthly bandwidth | 15 GB | Matches Google Drive free tier |
| Expiry | 2 hours | Cost control — files exist minutes, not days |
| Downloads | 10 | Enough for one-off sharing |
| Concurrency | 2 | Two simultaneous uploads |

Differentiators marketed:
- **Lossless:** No compression, no re-encode. Byte-identical delivery.
- **No recipient account:** Browser-only download.
- **Share before upload completes:** Streaming presigned URL.
- **E2E encryption:** AES-256-GCM, key never touches server.
- **Cloudflare speed:** Global edge network, no server bottleneck.

Ads shown on Free sender's recipient pages. Ad-rewarded bandwidth boosts available.

### Pro — ₫49,000/month or ₫490,000/year

| Limit | Value | Rationale |
|---|---|---|
| File size | 20 GB | Covers most video/archive use cases |
| Monthly bandwidth | 200 GB | ~₫78K worst-case cost, ₫49K price — break-even at ~60% utilization |
| Expiry | 30 days | Real storage, not delivery |
| Downloads | 100 | Generous for professional sharing |
| Concurrency | 5 | Multiple uploads for power users |

Additional features: Password protection, no ads, priority support.

### Studio — ₫199,000/month or ₫1,990,000/year

| Limit | Value | Rationale |
|---|---|---|
| File size | 100 GB | Raw footage, VM images, large datasets |
| Monthly bandwidth | 2 TB | Team/enterprise volume |
| Expiry | 90 days | Long-term availability |
| Downloads | Unlimited | No cap |
| Concurrency | 10 | Heavy parallel usage |

Additional features: Custom branding, API access, dedicated support.

## Config Structure (appsettings.json)

```json
{
  "Pricing": {
    "Currency": "VND",
    "ProMonthly": 49000,
    "ProYearly": 490000,
    "StudioMonthly": 199000,
    "StudioYearly": 1990000,
    "Limits": {
      "Free": {
        "MaxFileSizeBytes": 5368709120,
        "MaxMonthlyBytes": 16106127360,
        "MaxExpiryHours": 2.0,
        "MaxDownloads": 10,
        "MaxConcurrency": 2
      },
      "Pro": {
        "MaxFileSizeBytes": 21474836480,
        "MaxMonthlyBytes": 214748364800,
        "MaxExpiryDays": 30,
        "MaxDownloads": 100,
        "MaxConcurrency": 5
      },
      "Studio": {
        "MaxFileSizeBytes": 107374182400,
        "MaxMonthlyBytes": 2199023255552,
        "MaxExpiryDays": 90,
        "MaxDownloads": null,
        "MaxConcurrency": 10
      }
    }
  }
}
```

- `null` values = unlimited.
- `MaxExpiryHours` (Free) and `MaxExpiryDays` (Pro/Studio) are mutually exclusive per tier.
- `PlanTierLimits` reads config with fallback to these defaults if section is missing.

## Cost Analysis (R2 at ₫390/GB-month, ₫13/GB-day)

| Tier | Worst-case stored | Worst-case cost | Revenue | Margin |
|---|---|---|---|---|
| Free (2h TTL) | ~3.4 GB steady-state at full 15 GB/mo | ~₫1,326/mo | ₫0 (ads) | Ads cover |
| Pro (30d) | 200 GB at full cap | ₫78,000/mo | ₫49,000 | -₫29K worst case; typical ~30% util = ₫23K cost = 53% margin |
| Studio (90d) | 6 TB at full cap | ₫2,340,000/mo | ₫199,000 | Losing at worst case; typical enterprise util <10% = safe |

Studio worst case is theoretical — a single user uploading 2 TB every month for 90 days. Realistic utilization is far lower. If a user hits this, the "contact us for custom plan" FAQ provides an escape hatch.

## FAQ Updates

Updated FAQ answers for the new model:

**What's the catch with Free tier?**
Free tier is "instant delivery" — your file stays available for 2 hours. After that, it's permanently deleted. This lets us offer huge file sizes (5 GB) for free. Need longer? Upgrade to Pro.

**Why 2 hours for Free?**
Files stay alive long enough for the recipient to download, even large ones on slow connections. Storage costs money — by not storing files long-term, we keep Free truly free.

**Are my files compressed or reduced in quality?**
Never. What you upload is exactly what the recipient downloads — byte for byte. No compression, no re-encoding, no "storage saver." Photographers and video editors trust us for this reason.

**How fast are downloads?**
Files transfer directly through Cloudflare's global edge network — the same infrastructure powering millions of websites. No server bottleneck, no throttling.
