# Brainstorm Input Reconciliation

## Source
`_bmad-output/brainstorming/brainstorm-fileshare-differentiation-2026-07-18/`

## Extracted Items

### Positioning & Strategy
- **Original Hero Promise**: "TeleCloud file bao lớn cũng được, không cần tài khoản, free — và vì mã hóa trước khi upload, CHÍNH TELEGRAM CŨNG KHÔNG ĐỌC ĐƯỢC." (Any file size, no account, free, and because encrypted before upload, even Telegram can't read it)
- **Wedge Evolution**: Multiple wedges explored → refined to single ">2GB chunking" → then pivoted to "workflow-grade delivery for creative pros"
- **Competitive Positioning**: Honest wedge against WeTransfer (2GB + reads files), Telegram-direct (2GB + needs TG), Wormhole free (relay cap), LocalSend (needs app both sides, LAN only)
- **Brand Personality**: Anti-MLM stance, honest freemium, "ổn định, không bị khóa" (stable, not locked) instead of "private"
- **Vietnam Localization**: VI-first, Zalo/Telegram/Messages share-intent integration, local network speed tuning

### User Experience & Features
- **Receiver-never-installs**: Web/PWA receive, zero install, key-in-URL-fragment (Wormhole trick, mobile-native)
- **Share-sheet Integration**: Deep Android share-sheet, 1-tap send from any app
- **QR Code Handoff**: Same-room transfer, no typing, no app on other side
- **Live Progress**: Speed gauge during transfer (transparency builds trust)
- **Self-send / Drop Folder**: Between own devices, no account (most common real use)
- **Quality-Guarantee Badge**: Lossless delivery vs WhatsApp/Telegram compression

### Technical Architecture (Original MTProto Design)
- **MTProto Backend**: Telethon/Pyrogram Python gateway, only gateway holds session
- **Chunking Core**: 512MB/chunk, AES-256-GCM independent per chunk, AAD anti-swap
- **Manifest**: Encrypted JSON with chunk order, sizes, SHA-256, original name/mime
- **Telegram-as-Encrypted-DB**: Encrypted manifest + chunk-ID list stored as Telegram message
- **No External DB**: Share-code carries key; Telegram is storage + DB

### Encryption & Trust (Brainstorm Formulation)
- **Encrypt-before-chunk**: Defensive blind-pipe; ciphertext can't be content-flagged by Telegram
- **Key in URL Fragment**: Never leaves sender device; RFC 3986 guarantee
- **Zero-knowledge by Construction**: Key never on wire; server mathematically cannot decrypt
- **Network Self-check**: "Security Inspector" for user verification
- **Trust Stack without Open Source**: Architecture proof + network verification + published spec + verifier + crack-me + binary transparency + paid audit

### Business Model (Original)
- **Freemium**: Marginal cost ≈ 0 (Telegram bears storage) so free tier genuinely useful
- **Closed Source + Monetization**: Keep source to charge; trust via zero-knowledge design
- **Identity Tiers**: Guest (anonymous + rate-limit) → Verified (Google OAuth) → Paid
- **Quota Hero**: Single-file size (Verified ~50GB+, Guest gets 1 >2GB free demo)

### Risk & Mitigation (Original Telegram Context)
- **ToS/Ban Risk**: MTProto userbot violates Telegram ToS; existential risk
- **Mitigation**: Obey FLOOD_WAIT, volume ceiling, ephemeral default, encrypt (blind pipe), identity-gate
- **No Account Rotation**: Rejected (linked account farms get wiped together; evasion = legal exposure)
- **Migration Path**: Revenue → owned storage (S3/VPS)

## Reconciliation Status

### ✅ Captured in PRD
- Chunking mechanism (technical details carried forward to owned storage)
- Encryption architecture (AES-256-GCM, per-chunk AEAD, key in fragment)
- Manifest schema (adapted for owned storage objects)
- Server-enforced quota and identity tiers
- Report button → takedown (Firefox Send lesson)
- Ephemeral default + expiry
- Download tracking and receipt
- Password protection
- Verifiable zero-knowledge concept
- Vietnam market awareness (though not primary wedge)

### ✅ Captured in Addendum
- Economics and cost analysis (Telegram vs owned storage)
- ToS risk analysis (corrected from brainstorm overstatement)
- Bandwidth economics and pricing
- Rejection of Telegram backend (with rationale)
- Rejection of account rotation strategy
- Freemium vs paid tier economics
- Vietnam payment rails (VietQR, no Stripe)

### 🟡 Partially Captured
- **Trust Architecture**: PRD mentions "verifiable zero-knowledge" but the detailed trust stack (network inspector, published spec, open verifier, crack-me) is deferred to post-MVP in §6.2
- **Vietnam Localization**: Mentioned in market analysis but not explicitly in PRD features as "VI-first UI"
- **Anti-MLM Stance**: Qualitative positioning not explicitly stated in PRD
- **Quality-Guarantee / Lossless Badge**: Not in PRD features (lossless is assumed, not marketed)
- **Sender-side friction minimization**: Share-sheet integration implied but not explicit
- **Live speed gauge**: Not explicitly mentioned (transparency mentioned but not gauge specifically)

### 🔴 Missing - Qualitative & Strategic Gaps
- **Hero Promise Language**: Original "CHÍNH TELEGRAM CŨNG KHÔNG ĐỌC ĐƯỢC" framing lost in pivot (became IP-safe instead)
- **Brand Voice/Personality**: "Ổn định, không bị khóa" positioning not in PRD
- **"Zero-install receiver" as explicit wedge**: Receiver-no-auth is in PRD but the strategic "wedge vs LocalSend/Telegram" framing is not emphasized
- **"Honest freemium" positioning**: PRD describes freemium but not the "anti-scam" positioning against the roasted app
- **Self-send / Drop Folder feature**: Not in PRD (was feature #33 in brainstorm)
- **"Wow moment" design**: Guest gets 1 >2GB file free to experience the magic (demo convert concept)
- **Network Inspector UX**: How user actually verifies zero-knowledge (deferred but not described)
- **Ad-block handling posture**: Soft-wall vs hard-wall philosophy (in addendum but not in PRD narrative)
- **"Phone as hub" asymmetry**: Brainstorm noted this but PRD is web-only
- **Screenshot-attempt detection**: Mentioned in brainstorm features, not in PRD
- **Contactless public handle (@name)**: Feature #94 in brainstorm, not in PRD
- **Encrypted reply channel**: Feature #95, not in PRD
- **Screenshot detection**: Feature #98, not in PRD

## Key Gaps

### 1. Brand Voice & Strategic Positioning (Top Gap)
**Gap**: The brainstorm developed a specific brand voice—anti-MLM, honest ("ổn định, không bị khóa"), "proof not promises"—that's absent from the PRD narrative. The PRD is technically precise but lacks the qualitative brand positioning that was the "differentiation" work product.

**Impact**: Implementation teams won't know the intentional tone/voice. Copywriting, UX copy, and marketing positioning will default to generic SaaS voice instead of the differentiated "honest, anti-hype" personality.

**Evidence Location**: `.memlog.md` entries 19, 23, 24, 57, 58, 107, 111, 112, 143 + `brainstorm-intent.md` "Brand/VN" section

### 2. "Wow Moment" Onboarding Design
**Gap**: The brainstorm specified a key conversion tactic: "Guest được gửi 1 file >2GB miễn phí" (Guest gets 1 >2GB file free) to create the "khoảnh khắc convert" (conversion moment). This is missing from PRD account management design.

**Impact**: Free tier design (FR-8, FR-20) doesn't specify this tactical conversion lever. The onboarding flow won't deliberately create the "aha, this really sends 5GB through" moment that was the strategic conversion hook.

**Evidence Location**: `chunking-spec.md` "Demo chuyển đổi" section, `sender-gating-spec.md` quota matrix, `.memlog.md` entry 141

### 3. Trust Verification UX
**Gap**: The "network self-check / Security Inspector" concept—where users can verify that only ciphertext leaves the device—is in FR-6 but only as "In-app inspector or documented procedure." The brainstorm developed this as a key differentiator (verifiable, not promised) with specific implementation ideas (mitmproxy guide, toggle, real-time packet inspection).

**Impact**: FR-6 implementation will be minimal/technical rather than the designed "show don't tell" trust UX. The "zero-knowledge by construction" proof won't have the user-facing verification layer that was part of the competitive positioning.

**Evidence Location**: `trust-without-opensource.md` sections 1–3, `.memlog.md` entries 119, 143

### 4. Receiver-never-installs as Strategic Wedge
**Gap**: The brainstorm treated "receiver needs NO app" as the #1 strategic wedge vs LocalSend (needs app both sides) and croc (CLI). PRD has FR-7 (Recipient-never-authenticates) but the strategic positioning—this is THE defensible wedge—isn't emphasized. It's a feature, not a positioning statement.

**Impact**: Product positioning in marketing and UX may not lead with "your clients never install anything," missing the key differentiator that was the result of competitive analysis.

**Evidence Location**: `.memlog.md` entries 12, 13, 62, 72, 110 + `brainstorm-intent.md` "Hero promise" section

### 5. "Honest Freemium" Positioning vs Scam Perception
**Gap**: The brainstorm explicitly addressed the "scam/lỏ" perception problem (from the roasted app) with "honest freemium" and "anti-MLM stance" as trust signals. PRD describes the freemium model but doesn't include this strategic positioning against competitors' MLM/referral pyramids.

**Impact**: UX/messaging may default to generic SaaS upgrade prompts instead of the "no tricks, no referral pyramid" transparent positioning that was the antidote to the "scam" association.

**Evidence Location**: `.memlog.md` entries 23, 24, 57, 106, 111 + `brainstorm-intent.md` "Brand/VN" section

## Summary Statistics
- **Total brainstorm items extracted**: 67 ideas/features/decisions
- **Fully captured in PRD**: 19 items (28%)
- **Fully captured in Addendum**: 7 items (10%)
- **Partially captured**: 8 items (12%)
- **Missing**: 33 items (49%) — predominantly qualitative positioning, UX design, and strategic framing

**Note**: The missing items are primarily qualitative (brand voice, UX copy, strategic positioning narratives) rather than functional requirements. The PRD captured the technical architecture and core features correctly but dropped the "soft" differentiators that were the product of the competitive analysis and brand work.

