# Pricing Impact — P2P Delivery

> Companion to SPEC-webrtc-p2p. Describes how P2P changes the existing pricing model defined in `spec-pricing-page/pricing-tiers.md`.

## Core Change

P2P transfer limits are **decoupled from server transfer limits**. A user can send unlimited files via P2P regardless of tier — only server-based fallback consumption is capped by tier.

## Tier Breakdown (Updated)

| Factor | Free ($0) | Starter ($8/mo) | Ultimate ($23/mo) |
|--------|-----------|-----------------|-------------------|
| **P2P file size** | Unlimited | Unlimited | Unlimited |
| **P2P transfers** | Unlimited | Unlimited | Unlimited |
| **Server fallback size** | 200 MB/file | 750 MB/file | 1.5 GB/file |
| **Server fallback volume** | 3 GB/mo | 300 GB/mo | Unlimited |
| **Server transfers/mo** | 10 | 10 | Unlimited |
| **Recipient must be online?** | Yes (P2P) or within quota | No (server fallback always available) | No |
| **Ads** | Wallpaper ads on download pages | No ads | No ads |
| **Password protection** | ❌ | ✅ | ✅ |
| **Custom branding** | ❌ | ❌ | ✅ |
| **Malware scanning** | ❌ | ❌ | ✅ |

## Monetization Narrative

The free tier is ad-supported. Users who want:
- **No ads** + **password protection** → Starter
- **Everything unlimited** + **branding** + **malware scan** → Ultimate
- **Server fallback for offline recipients** → any tier (with quota)

P2P being unlimited free is a **user-retention and competitive-parity decision** (vs LocalSend). Revenue comes from:
1. Ads on free-tier download pages
2. Subscriptions for server features, no-ads, and premium capabilities
