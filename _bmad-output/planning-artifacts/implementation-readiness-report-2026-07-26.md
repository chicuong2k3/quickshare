---
stepsCompleted:
  - step-01-document-discovery
  - step-02-prd-analysis
  - step-03-epic-coverage-validation
  - step-04-ux-alignment
  - step-05-epic-quality-review
  - step-06-final-assessment
includedFiles:
  prd: prds/prd-TeleCloud-2026-07-19/prd.md
  prdAddendum: prds/prd-TeleCloud-2026-07-19/addendum.md
  architecture: architecture/architecture-P2P-2026-07-20/ARCHITECTURE-SPINE.md
  epics: epics.md
  uxDesign: ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md
  uxExperience: ux-designs/ux-TeleCloud-2026-07-19/EXPERIENCE.md
---

# Implementation Readiness Assessment Report

**Date:** 2026-07-26
**Project:** TeleCloud

## Document Inventory

### PRD
- `prds/prd-TeleCloud-2026-07-19/prd.md`
- `prds/prd-TeleCloud-2026-07-19/addendum.md`

### Architecture
- `architecture/architecture-P2P-2026-07-20/ARCHITECTURE-SPINE.md`

### Epics
- `epics.md`

### UX
- `ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md`
- `ux-designs/ux-TeleCloud-2026-07-19/EXPERIENCE.md`

---

## PRD Analysis

**PRD Source:** `prds/prd-TeleCloud-2026-07-19/prd.md` + `addendum.md`
**Product:** ZapDrop (formerly TeleCloud) — renamed 2026-07-19 after storage backend pivot

### Functional Requirements

| ID | Description | MVP? |
|----|-------------|------|
| FR-1 | Split-and-store any-size file (chunked, owned storage) | Yes |
| FR-2 | Parallel upload with chunk-level resume after connection drop | Yes |
| FR-3 | Download reassembly with SHA-256 integrity verification per-chunk + whole-file | Yes |
| FR-4 | Encrypt-before-upload: AES-256-GCM, key in URL fragment only | Yes |
| FR-5 | Independent per-chunk AEAD (unique nonce, AAD binds manifest_id + chunk index) | Yes |
| FR-6 | Verifiable zero-knowledge: network-inspectable proof that only ciphertext leaves client | Yes |
| FR-7 | Recipient-never-authenticates: download with no account or login | Yes |
| FR-8 | Sender accounts: sign-in, Free by default, upgradeable to Pro/Studio | Yes |
| FR-9 | Server-enforced quota per Plan (file size, monthly volume, max expiry, download cap, concurrency) | Yes |
| FR-10 | Link expiry: sender sets expiration; expired → 410 + storage reclaimed | Yes |
| FR-11 (a) | Download-count cap per link; locks when count reached (note: FR-11 duplicated in PRD) | Yes |
| FR-12 (a) | Paid delivery via VietQR local payment rails (note: FR-12 duplicated in PRD) | Yes |
| FR-13 | Egress passthrough (Studio tier): overage billing per-GB beyond included allowance | No (deferred) |
| FR-11 (b) | Password protection: server-side gate before Manifest resolution | Yes |
| FR-12 (b) | Branded delivery page: logo, background, message per-Delivery | Yes |
| FR-14 | Download tracking/receipt: count + last-download timestamp visible to sender | Yes |
| FR-15 | File management: list/search/sort/paginate Deliveries + folders (flat hierarchy, CRUD) | Yes |
| FR-18 | Rate & volume guardrails: server-enforced per-Plan limits, metadata-only logging | Yes |
| FR-19 | Report → takedown: anyone reports a link → Chunks + Manifest deleted → 410 | Yes |
| FR-20 | Plan-scoped ad placement: ads only on Free-sender recipient download pages | Yes |
| FR-21 | Clean trial allowance: 3–5 ad-free deliveries for new accounts | Yes |
| FR-22 | Anti-adblock recovery/soft-wall (not hard-block) | Yes |

**⚠️ Duplicate FR numbering:** FR-11 and FR-12 appear twice in the PRD (§4.4 and §4.5). Above marked as (a)/(b). **Total: 19 unique FRs** (FR-1 through FR-22, missing FR-16, FR-17; FR-11 and FR-12 duplicated).

### Non-Functional Requirements

| ID | Description | Source |
|----|-------------|--------|
| NFR-1 | Chunk throughput optimized for Vietnamese network conditions; chunk-size × concurrency defaults benchmarked before locking | §4.1 |
| NFR-2 | Pricing tier matrix is configuration, tunable without code changes | §4.4 |
| NFR-3 | Free-tier egress must stay capped (FR-10 + FR-11); ads reduce loss, do not make Free profitable | §4.8 |
| NFR-4 | AdSense unusable (policies bar file-sharing); monetization via download-niche networks (Adsterra/PopAds/Monetag-class) under category filters | §4.8 |

### Additional Requirements & Constraints

- **Storage:** Owned dedicated server with disk, cold overflow (Backblaze) — not Telegram
- **Auth:** Google OAuth (existing wiring)
- **Payments:** VietQR first (lowest-friction VN rail); MoMo/ZaloPay deferred; Stripe unavailable in VN
- **Pricing:** Free / Pro (~$2–4/mo) / Studio (~$8–12/mo); flat-fee tiers survive via short expiry + download cap + egress passthrough (Studio)
- **Tech stack:** .NET 10, Blazor, FastEndpoints, WolverineFx, EF Core, PostgreSQL, RizzyUI — unchanged
- **Security:** Client-side E2E, crypto spec + open verifier + audit post-MVP
- **No:** Stripe, sync, native mobile v1, teams/SSO, anonymous mass hosting

### PRD Completeness Assessment

- **Strong:** Market research, competitive analysis, economics, and abuse control are well-reasoned with sourced evidence
- **Warning:** Duplicate FR-11/FR-12 numbering; FR-16 and FR-17 missing (gaps in numbering)
- **Warning:** Two unsolved problems are explicitly non-technical (market validation + distribution) — PRD acknowledges these remain open
- **Gap:** The PRD covers the full product vision but the current implementation is incremental. No mapping of which FRs are "already done" vs "yet to build" — this is epics' responsibility

---

## Epic Coverage Validation

### Coverage Matrix

| FR | PRD Requirement | Epic Coverage | Status |
|----|----------------|---------------|--------|
| FR-1 | Split-and-store any-size file (chunked) | Epic 1 (Story 1.3) | ✓ Covered |
| FR-2 | Parallel upload + chunk-level resume | Epic 1 (Story 1.3) | ✓ Covered |
| FR-3 | Download reassembly + SHA-256 verification | Epic 1 (Story 1.7) | ✓ Covered |
| FR-4 | Encrypt-before-upload, key in URL fragment | Epic 1 (Story 1.5) | ✓ Covered |
| FR-5 | Independent per-chunk AEAD | Epic 1 (Story 1.5) | ✓ Covered |
| FR-6 | Verifiable zero-knowledge | Epic 2 (Story 2.4) | ✓ Covered |
| FR-7 | Recipient-never-authenticates | Epic 1 (Stories 1.4, 1.7) | ✓ Covered |
| FR-8 | Sender accounts (Google OAuth + Plans) | Epic 1 (Story 1.2) | ✓ Covered |
| FR-9 | Server-enforced quota per plan | Epic 1 (Story 1.2) | ✓ Covered |
| FR-10 | Link expiry + storage reclamation | Epic 1 (Story 1.8) | ✓ Covered |
| FR-11 (a) | Download-count cap per link | Epic 1 (Story 1.8) | ✓ Covered |
| FR-12 (a) | Paid delivery via VietQR | Epic 2 (Story 2.3) | ✓ Covered |
| FR-13 | Egress passthrough (Studio tier) | — (deferred, post-MVP) | ⚠️ Noted |
| FR-11 (b) | Password protection (server-side gate) | Epic 2 (Story 2.1) | ✓ Covered |
| FR-12 (b) | Branded delivery page (Pro/Studio) | Epic 2 (Story 2.2) | ✓ Covered |
| FR-14 | Download tracking (count + timestamp) | Epic 1 (Story 1.8) | ✓ Covered |
| FR-15 | File management CRUD + folders | Epic 1 (Story 1.9) | ✓ Covered |
| FR-18 | Rate & volume guardrails | Epic 2 (Story 2.5) | ✓ Covered |
| FR-19 | Report → takedown | Epic 2 (Story 2.6) | ✓ Covered |
| FR-20 | Plan-scoped ad placement (Free only) | Epic 2 (Story 2.7) | ✓ Covered |
| FR-21 | Clean trial allowance | Epic 2 (Story 2.7) | ✓ Covered |
| FR-22 | Anti-adblock soft-wall | Epic 2 (Story 2.7) | ✓ Covered |

### Coverage Statistics

- **Total PRD FRs:** 19 unique (including duplicate FR-11/FR-12)
- **FRs covered in epics:** 19
- **Coverage:** 100%
- **Deferred:** FR-13 (Egress passthrough — explicitly post-MVP)

### Critical Gaps

#### 🔴 PLAN UPGRADE / SUBSCRIPTION FLOW — NOT IN PRD OR EPICS

**The PRD and epics DO NOT cover plan upgrade subscriptions.** This is the work you were just starting:

- **PayOS payment flow**: User on `/upgrade` picks a plan → redirects to PayOS → PayOS webhook confirms → user's `User.Plan` field updates
- **Plan lifecycle**: subscription created → plan set to "pro"/"studio", cancelled → grace period, expired → drop to "free"
- **`/api/upgrade/initiate` endpoint** and **`/api/payments/payos/webhook` endpoint** — not in any epic

The PRD's FR-12/VietQR is about *recipient-paid downloads per link*, not sender-paid plan subscriptions. These are two distinct payment flows, and the plan-upgrade flow is entirely missing from planning artifacts.

#### 🟡 Tech Stack Mismatch

Epics reference `src/QuickShare.Client/src/features/...` (React/TypeScript), but the current codebase is Blazor (.NET) with Razor components. EXPERIENCE.md correctly specifies "Blazor SSR + RizzyUI" — epics are out of sync.

#### 🟡 Architecture Scope

Architecture doc only covers P2P (`architecture-P2P-2026-07-20/ARCHITECTURE-SPINE.md`). No architecture exists for the core delivery pipeline, payment flows, or plan subscription system.

---

## UX Alignment Assessment

### UX Document Status
**Found:** `ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md` + `EXPERIENCE.md`

### UX ↔ PRD Alignment
- ✅ UX covers all PRD user journeys (UJ-1 through UJ-4)
- ✅ UX flows map to PRD FRs: upload (FR-1/2), download (FR-3/7), password (FR-14), payment (FR-12), ads (FR-20–22)
- ✅ DESIGN.md color tokens, typography, spacing match the Epics Story 1.10 design system
- ✅ EXPERIENCE.md specifies Blazor SSR + RizzyUI (matches current codebase)
- ✅ Microcopy patterns defined in Vietnamese + English
- 🟡 No UX flow for the plan upgrade/checkout experience (`/upgrade` page, PayOS redirect, post-payment landing)

### UX ↔ Architecture Alignment
- ✅ EXPERIENCE.md specifies SSR with antiforgery, matching the existing FastEndpoints pattern
- ⚠️ Architecture doc missing for core delivery pipeline (R2 storage), so UX → Architecture alignment cannot be fully verified
- 🟡 No UX for payment confirmation flow (PayOS webhook → plan update → redirect to homepage)

### Warnings

| Severity | Issue |
|----------|-------|
| 🔴 | **No UX exists for the plan upgrade flow** — `/upgrade` page, plan selection summary card, PayOS redirect, post-payment homepage redirect. This is the work you're currently building but it has no UX design contract. |
| 🟡 | Epics reference React/TypeScript client (`src/QuickShare.Client/src/...`) but EXPERIENCE.md and current codebase use Blazor/Razor. Epics need tech-stack correction. |
| 🟡 | `/account/billing` is listed as "future" in site structure — no design for `/upgrade` at all. |
| 🟡 | UX covers VietQR per-delivery payment (recipient pays to download), not plan subscription (sender pays for plan tier). These are different flows. |

---

## Epic Quality Review

### Epic Structure

| Epic | User-Centric? | Stands Alone? | Verdict |
|------|:---:|:---:|---------|
| Epic 1: Core Delivery Pipeline | ✅ Sender workflows | ✅ Yes | Pass |
| Epic 2: Professional Delivery & Safety | ✅ Extends delivery features | ✅ Builds on Epic 1 | Pass |

### Story-by-Story Assessment

**Epic 1:**

| Story | User Value? | AC Quality | Issues |
|-------|:---:|------|---------|
| 1.1 R2 Infrastructure | 🟡 Technical | ✅ BDD | No direct user benefit — plumbing story |
| 1.2 Auth & Plan Tiers | ✅ | ✅ BDD | Plan limits hardcoded in AC (should ref config) |
| 1.3 R2 Multipart Upload | 🟡 Mixed | ✅ Detailed BDD | Implicit dep on 1.1 for R2 provider |
| 1.4 Download 302 Redirect | ✅ | ✅ Range + TTL + edge cases | Good byte-range resume coverage |
| 1.5 Client-Side Encryption | ✅ | ✅ BDD | WebCrypto + Web Worker, key in fragment |
| 1.6 Upload UI | ✅ | ✅ Visual states | Drag-drop, retry, mobile state |
| 1.7 Download UI | ✅ | ✅ Error/expiry states | Chunk re-fetch on auth failure |
| 1.8 Link Lifecycle | 🟡 Mixed | ✅ BDD | System job + user cap enforcement |
| 1.9 Dashboard | ✅ | ✅ BDD | Skeleton, empty, search, CRUD |
| 1.10 Design Tokens | 🔴 Technical | ❌ No ACs | Pure CSS setup, zero user value alone |
| 1.11 Epic 2 Template Hooks | 🔴 Architectural | ❌ No BDD | **Forward dependency** on Epic 2 features |

**Epic 2:**

| Story | User Value? | AC Quality | Issues |
|-------|:---:|------|---------|
| 2.1 Password Protection | ✅ | ✅ Strict BDD | bcrypt, server-side gate, 401 |
| 2.2 Branded Delivery | ✅ | ✅ BDD | Logo/color/message, Free = nothing |
| 2.3 VietQR Payment | ✅ | ✅ BDD + polling | Timer, timeout, retry, webhook |
| 2.4 Trust Verification | ✅ | ✅ BDD | Progressive disclosure, DevTools guide |
| 2.5 Rate/Volume Guardrails | 🟡 Operator | ✅ BDD | 429/403 headers, volume warnings |
| 2.6 Report → Takedown | ✅ | ✅ BDD | Immediate 410, no restore |
| 2.7 Ads + Trial + Adblock | ✅ | ✅ BDD | Interstitial, clean trial, soft-wall |
| 2.8 Branding Settings UI | ✅ | ✅ BDD + UX | Logo upload, color picker, preview |

### Violations

#### 🔴 Critical

| Issue | Fix |
|-------|-----|
| **Plan upgrade/subscription entirely missing** — no epic or story for PayOS flow, `/api/upgrade/initiate`, webhook, plan lifecycle | Add Epic 3: "Sender upgrades plan via PayOS subscription" |
| **Story 1.11 forward dependency** on Epic 2 features | Move to Epic 2 as Story 2.0, or make slots truly nullable |
| **No pricing page story** — `/pricing` exists in codebase but not in epics | Add story covering billing toggle, plan cards, CTA routing |

#### 🟠 Major

| Issue | Fix |
|-------|-----|
| Story 1.10 has no ACs | Add BDD ACs or merge into 1.6 |
| Tech stack: all `Files affected` point to React (`src/QuickShare.Client/src/...`) but codebase is Blazor/Razor | Update paths to match actual project |
| Plan limits hardcoded in 1.2 ACs | Reference config file instead of specific numbers |

### Best Practices Compliance

- [x] Epics deliver user value (both pass, 1.1/1.10 borderline)
- [x] Epic independence (2 builds on 1, no circular deps)
- [⚠️] Story sizing (1.11 too broad; 1.10 has no ACs)
- [⚠️] Forward dependency exists (1.11 → Epic 2)
- [x] No upfront "create all tables" story
- [⚠️] AC quality varies (1.10/1.11 not BDD)
- [x] FR traceability maintained (coverage map exists)

---

## Summary and Recommendations

### Overall Readiness Status

## 🔴 NOT READY — Plan Upgrade Payment Flow

The existing planning artifacts cover the full ZapDrop product vision (chunked upload, R2 storage, encrypted delivery, VietQR per-link payments, ads) but **do not cover the plan subscription/upgrade flow** you're currently building. This is the single most critical gap.

### Assessment by Category

| Category | Status | Key Finding |
|----------|:---:|-------------|
| **PRD** | ✅ Complete (for ZapDrop) | 19 FRs, strong market research; plan upgrade omitted |
| **Architecture** | ⚠️ Incomplete | Only P2P covered; no core pipeline or payment architecture |
| **Epics** | ⚠️ Partial | 100% FR coverage for ZapDrop; **0% for plan upgrade** |
| **UX** | ⚠️ Partial | Strong for ZapDrop; **no UX for `/upgrade`, PayOS flow** |
| **Epic Quality** | 🟡 Minor issues | 2 stories without ACs, 1 forward dep, React→Blazor mismatch |

### Critical Issues Requiring Immediate Action

| # | Issue | What to do |
|---|-------|------------|
| 1 | **No plan upgrade in PRD/Epics/UX** — PayOS payment flow, plan lifecycle, webhook handling, post-payment redirect are unspecced | Complete the `bmad-spec` you started, then feed it into PRD/Epics. Or build a minimal Epic 3 directly. |
| 2 | **Architecture gap** — no architecture for payment endpoints, webhook security, plan state machine | Create a lightweight architecture spine for the payment module |
| 3 | **WHAT YOU'RE CURRENTLY BUILDING vs WHAT'S SPECCED** — the `/upgrade` page, Pricing.razor, PayOS integration are being built without planning artifacts. This creates drift. | Either: (a) finish the spec → PRD → epics chain, or (b) accept building payment as a standalone mini-feature and bypass the full planning pipeline |

### Recommended Next Steps

1. **Complete the payment spec** (`/bmad-spec` — you were mid-guided mode) to define PayOS redirect + webhook flow and plan lifecycle
2. **Create Epic 3 Plan Subscription** with 2-3 stories: Initiate PayOS payment, Receive PayOS webhook, Update plan state
3. **Add UX for `/upgrade` page** — plan summary card, PayOS redirect flow, post-payment confirmation
4. **Fix tech-stack references** in epics — replace `src/QuickShare.Client/src/...` with actual Blazor Razor paths
5. **Fix Story 1.10/1.11** — add BDD ACs or merge into parent stories

### Final Note

This assessment identified **8 issues** across 5 categories. The core ZapDrop product (delivery pipeline, encryption, branding, paid downloads) is well-planned. The plan upgrade/subscription flow — the work you're doing right now — is a blind spot across all artifacts. Decide whether to run it through the full planning pipeline or treat it as a focused implementation outside the existing epics.

---

**Report:** `_bmad-output/planning-artifacts/implementation-readiness-report-2026-07-26.md`
**Assessor:** bmad-check-implementation-readiness
**Date:** 2026-07-26







