---
id: SPEC-pricing-page
companions:
  - pricing-tiers.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# QuickShareCloud — Pricing & Tier Limits (Revamp 2026-08)

## Why

Current pricing is not competitive. Free tier (100 MB/file, 2 GB/month, 7-day expiry) loses to Google Drive (15 GB free, permanent) and Telegram (2 GB/file, unlimited storage) on every metric that matters to a first-time user. The pricing page still references a deprecated 5-tier USD model (Free/Starter/Ultimate/Teams/Enterprise) that never shipped.

**Strategic pivot:** Stop competing on storage GB (unwinnable). Compete on *transfer capability* — file size, speed, lossless quality, and zero-recipient-friction. Free tier becomes "instant delivery" (files auto-delete after short TTL), paid tiers are "storage + branding."

## Competitive Differentiators (non-storage)

| Differentiator | Google Drive | Telegram | **QuickShareCloud** |
|---|---|---|---|
| Max file size (free) | 15 GB total, no per-file cap but slow | 2 GB hard cap | **5 GB** |
| Share before upload completes | No — must wait for progress bar | No | **Yes** (streaming presigned URL) |
| Recipient needs account | Yes (Google) | Yes (Telegram app) | **No** — browser only |
| Lossless (no re-encode) | Photos: no. Drive: yes but slow. | Default: compresses | **Always** — byte-identical |
| E2E encryption | No (server holds keys) | Cloud chat: no | **Yes** — AES-256-GCM, key in URL fragment |
| Download speed bottleneck | Google edge | Telegram DC | **Cloudflare global edge** (R2 presigned URL) |

## Capabilities

### CAP-1: Free tier — instant delivery model
- **intent:** Free users can send files up to 5 GB. Files auto-expire after 2 hours (configurable). No long-term storage — this is "send and forget."
- **success:** Free user uploads 5 GB file, gets share link. Recipient downloads. After TTL expires, link returns 410. R2 objects reclaimed by background job.

### CAP-2: Paid tiers — real storage
- **intent:** Pro (30-day) and Studio (90-day) tiers offer real cloud storage with higher limits. Files persist for the full duration. Password protection, branding, and higher concurrency included.
- **success:** Pro/Studio user uploads file, link stays alive for full expiry period. Recipient can download anytime within window. Limits enforced server-side.

### CAP-3: Config-driven limits
- **intent:** All tier limits (file size, monthly bandwidth, expiry duration, downloads, concurrency) are configurable in `appsettings.json` under `Pricing:Limits`. No hardcoded magic numbers.
- **success:** Changing a value in appsettings.json changes the enforced limit at runtime (after restart). `PlanTierLimits` reads from `IConfiguration` with sensible defaults.

### CAP-4: Pricing page reflects current tiers
- **intent:** `/pricing` page displays Free/Pro/Studio with correct limits, prices in VND, and competitive messaging (file size, no-recipient-account, lossless, E2E).
- **success:** Page renders 3 tiers. Yearly toggle works (Alpine.js, no server round-trip). Feature lists match `PlanTierLimits`. CTA buttons route correctly.

### CAP-5: Lossless quality messaging
- **intent:** Marketing copy and download page communicate that files are never re-encoded, compressed, or altered. "What you send is what they receive — byte for byte."
- **success:** Home page and pricing page include lossless messaging. Download page includes "byte-identical" verification info in the E2E panel.

## Tier Limits (source of truth: `pricing-tiers.md`)

| Limit | Free | Pro | Studio |
|---|---|---|---|
| File size | 5 GB | 20 GB | 100 GB |
| Monthly bandwidth | 15 GB | 200 GB | 2 TB |
| Expiry | 2 hours | 30 days | 90 days |
| Max downloads | 10 | 100 | Unlimited |
| Max concurrency | 2 | 5 | 10 |
| Price/month (VND) | 0 | 49,000 | 199,000 |
| Price/year (VND) | 0 | 490,000 | 1,990,000 |

## Constraints

- All limits MUST be configurable in `appsettings.json` under `Pricing:Limits:<tier>:<field>`. `PlanTierLimits` reads config with fallback defaults.
- Expiry is stored as `TimeSpan` internally (not `int` days). Free tier uses hours, paid tiers use days — unified under one type.
- `CommitR2Handler` and `StartR2Handler` call `PlanTierLimits.GetMaxExpiry(plan)` returning `TimeSpan`, applied to `ExpiresAt`.
- `ExpiresAt` column remains `DateTime` — no schema change needed.
- Pricing page uses Alpine.js for billing toggle (static data, no server round-trip per AGENTS.md HTMX rule).
- Must use VND currency. Prices are integers (no decimals for VND).

## Non-goals

- No changes to P2P limits (P2P is same-WiFi only, not in competitive scope).
- No changes to payment processing, subscription lifecycle, or PayOS integration.
- No Team/Enterprise tiers at this time.
- No per-user configurable expiry (sender picks from plan ceiling, not arbitrary).

## Success signal

Free user uploads 5 GB video, sends link via Zalo, recipient downloads immediately while upload still in progress. Link returns 410 after 2 hours. Pro user uploads 20 GB project, link stays alive 30 days with password protection. Pricing page at `/pricing` shows correct VND prices, limits match config. All limits changeable via `appsettings.json` without recompilation.

## Assumptions

- R2 storage cost (₫390/GB-month) makes free tier "instant delivery" model economically viable — files exist for minutes/hours, not days.
- Cloudflare R2 egress is $0 — download bandwidth costs nothing.
- Free tier users are ad-supported; ad revenue covers the near-zero storage cost of 2-hour TTL.
- Streaming upload (share before upload completes) is already implemented via presigned URLs — this spec does not add it, only markets it.
