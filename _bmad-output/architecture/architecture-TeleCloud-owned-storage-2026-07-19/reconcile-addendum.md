# Addendum Reconciliation

## Source
`_bmad-output/planning-artifacts/prds/prd-TeleCloud-2026-07-19/addendum.md`

## Constraint Coverage Analysis

### ✅ Architecturally addressed

**Storage Economics (§B)**
- Owned dedicated disk selection (~$115/mo vs Telegram ~$210/mo) → AD-1 (storage provider abstraction), AD-13 (local disk primary, Backblaze B2 overflow)
- Cost structure validated: 85-88% gross margin achievable → AD-14 (background cleanup), AD-3 (24-hour upload session TTL)
- Telegram rejection economics (2× upload-push penalty) → AD-8 (clean migration from Telegram)

**Bandwidth Economics (§C)**
- Bandwidth as near-fixed cost on commodity hardware → AD-1 (IStorageProvider interface enables provider switching)
- Egress cost spread ($0-$90/TB) → AD-13 (configuration-driven provider selection)

**Chunking Architecture (§H)**
- Encrypt-before-chunk with independent AES-256-GCM AEAD units → AD-2 (three-phase chunked upload), AD-4 (idempotent chunk operations)
- Manifest storage with chunk references → AD-7 (manifest as bytea in PostgreSQL)
- Atomic commit with resume capability → AD-5 (transaction boundaries), AD-3 (UploadSession state)

**Payment Architecture (§E, §F)**
- Stripe unavailable in Vietnam → AD-11 (PayOS webhook primary)
- Transaction-based delivery unlock → AD-11 (webhook signature verification)

**Vietnam Market (§F)**
- Localization as table-stakes, not moat → Implicitly addressed by Blazor SSR + existing UI architecture
- Payment rails (VietQR, MoMo, ZaloPay) → AD-11 defers secondary integration appropriately

**Abuse Prevention (§G, FR-18/FR-19)**
- Firefox Send lesson (E2E + no abuse button = ransomware haven) → AD-10 (IP-based tracking, report button)
- Metadata-based moderation → AD-10 (download tracking without content access)

**Rejected Alternatives (§I)**
- Telegram as paid backend → AD-8 (clean break migration, legacy tables deprecated)
- Hybrid overflow to Telegram → AD-13 (Backblaze B2 as cold overflow only)
- Cloud object storage (R2/S3) as primary → AD-1 (local disk primary via configuration)
- Account rotation → Implicitly rejected via AD-8 (no Telegram)

### 🟡 Partially addressed

**Download-Count Cap Architecture**
- Addendum constraint: "download-count cap/link" is critical solvency lever (§D)
- Architecture coverage: AD-10 references FR-18 download tracking but cap enforcement not architecturally invariant
- Gap: Download-count limit should be architecturally invariant at Delivery entity level, not handler implementation detail
- Risk: Without invariant cap, bandwidth costs can spiral (avg 1.5 downloads/link is baseline, pro users may exceed)

**Ad Network Integration (§G2)**
- Addendum constraint: Ads only on recipient download page for Free-sender deliveries (cleanliness follows sender's Plan)
- Architecture coverage: Deferred to delivery page implementation (AD-14 "Deferred" section)
- Gap: Ad injection point is architecturally significant (recipient page templating), not just implementation
- Risk: Implementing ads incorrectly could dirty Pro/Studio deliveries, breaking the "clean handoff" value proposition

**Egress Passthrough Architecture**
- Addendum constraint: "egress passthrough at top tier" is solvency lever (§D), MASV-style per-GB billing transparency
- Architecture coverage: Not mentioned in spine invariants
- Gap: High-tier egress accounting and passthrough billing should be architecturally invariant
- Risk: Without egress tracking at architectural level, Studio tier becomes economic loss leader

**Storage Overflow Trigger**
- Addendum constraint: "Correct overflow = local disk → Backblaze B2" (§B.3)
- Architecture coverage: AD-14 defers trigger thresholds to operational policy
- Gap: Overflow trigger logic is architecturally significant (automatic vs manual, capacity thresholds)
- Risk: Manual overflow or unclear thresholds could cause unexpected costs or service interruption

**Anti-Adblock Strategy**
- Addendum constraint: "Ad-Shield-style recovery and/or soft-wall, NOT hard-block" (§G2)
- Architecture coverage: Not mentioned in spine
- Gap: Anti-adblock posture affects recipient page architecture (subdomain usage, detection logic)
- Risk: Hard-block implementation would false-block legitimate users (Brave/Firefox-privacy/DNS-filter)

### 🔴 Not architecturally invariant (correctly deferred)

**Tier-Specific Limits**
- File size caps (5GB free, 50GB Pro, unlimited Studio) → Handler-level validation, not invariant
- Expiry limits (3 days free, 30 days paid) → Delivery entity field, not invariant
- Concurrent upload limits (1-2 free, 3-4 Pro, 6+ Studio) → Handler-level implementation

**Ad Network Selection**
- Specific networks (Adsterra, PopAds, Monetag, etc.) → Implementation detail
- CPM optimization, impression counting → Handler-level business logic

**Payment Network Selection**
- Specific QR generation, bank integration flows → Implementation detail

**Chunk Size Calibration**
- Optimal chunk size (256-512MB) → Configuration-driven, not invariant

**Webhook Security Details**
- PayOS HMAC signature verification mechanism → Implementation detail

## Critical Gaps

### 1. Missing Download-Count Cap Invariant
**Severity:** HIGH  
**Issue:** Architecture spine lacks invariant for download-count limit enforcement at Delivery entity level  
**Addendum basis:** "Download-count cap/link" is one of three solvency levers; MASV uses per-transfer caps; incumbent analysis shows vendors eat re-download overage without caps  
**Architectural impact:** Should add AD-XX for `Delivery.MaxDownloads` field with invariant enforcement at API level (not handler implementation)  
**Risk:** Without invariant, bandwidth economics spiral (75 TB served → 150 TB at 2× avg downloads)

### 2. Missing Egress Accounting Architecture
**Severity:** HIGH  
**Issue:** Studio tier "egress passthrough" requires byte-level accounting per Delivery, but no invariant exists  
**Addendum basis:** "Egress passthrough at top tier" is solvency lever; MASV per-GB billing prevents surprise bills; wholesale cost $0-10/TB vs retail $250/TB  
**Architectural impact:** Should add AD-XX for `Delivery.EgressBytes` tracking with plan limit enforcement  
**Risk:** Studio tier becomes economic loss leader; can't implement "egress passthrough over allowance"

### 3. Ad Injection Point Architecture
**Severity:** MEDIUM  
**Issue:** Recipient page ad injection only for Free-sender deliveries is architecturally significant but deferred  
**Addendum basis:** "Cleanliness follows the sender's Plan: Pro/Studio → recipient clean (a sold feature); Free → recipient sees ads"  
**Architectural impact:** Should add AD-XX for recipient page rendering based on `User.PlanId` lookup, not handler-level logic  
**Risk:** Implementing ads at handler level could dirty Pro/Studio deliveries, breaking "ad-free handoff" value proposition

### 4. Overflow Trigger Architecture
**Severity:** MEDIUM  
**Issue:** Storage overflow trigger (local disk → Backblaze B2) is operational policy, not architectural decision  
**Addendum basis:** "Correct overflow = local disk → Backblaze B2, never Telegram" (§B.3); hybrid overflow to Telegram rejected as backwards  
**Architectural impact:** Should add AD-XX for automatic overflow trigger at storage provider interface level  
**Risk:** Manual overflow or unclear thresholds cause unexpected costs or service interruption at 100-user scale

### 5. Anti-Adblock Posture
**Severity:** LOW  
**Issue:** Anti-adblock strategy (soft-wall vs hard-block) affects recipient page architecture but not specified  
**Addendum basis:** "Ad-Shield-style recovery and/or soft-wall, NOT hard-block"; hard-block false-legitimates Brave/Firefox-privacy users  
**Architectural impact:** Should add AD-XX for recipient page anti-adblock posture (soft-wall with retry, no hard-block)  
**Risk:** Hard-block implementation false-legitimates privacy-focused users; contradicts gigafile's approach

## Summary Statistics

- **Total constraints analyzed:** 27
- **Fully addressed:** 17 (63%)
- **Partially addressed:** 5 (19%)
- **Correctly deferred:** 5 (19%)
- **Critical gaps:** 5 (3 HIGH, 2 MEDIUM severity)

**Coverage assessment:** Architecture spine successfully captures core economic and technical constraints (storage choice, chunking, abuse prevention). Gaps exist in **tier differentiation enforcement** (download caps, egress accounting) and **recipient page architecture** (ad injection, anti-adblock). These should be elevated to architectural invariants before implementation to prevent economic drift.