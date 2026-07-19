# PRD Reconciliation

## Source
`_bmad-output/planning-artifacts/prds/prd-TeleCloud-2026-07-19/prd.md`

## FR Coverage Analysis

### ✅ Fully covered in spine: FR-1, FR-2, FR-3, FR-15, FR-18, FR-19
- **FR-1 (Split-and-store any-size file)**: Fully covered by AD-1 (Storage Provider Abstraction), AD-2 (Chunked Upload Flow Architecture), AD-6 (Entity Relationship Model), AD-7 (Manifest Storage Location)
- **FR-2 (Parallel upload with chunk-level resume)**: Fully covered by AD-2 (Chunked Upload Flow Architecture), AD-3 (UploadSession Resume State), AD-4 (Idempotent Chunk Operations), AD-5 (Transaction Model Boundaries)
- **FR-3 (Download reassembly with integrity verification)**: Fully covered by AD-1 (Storage Provider Abstraction), AD-2 (Chunked Upload Flow Architecture), AD-5 (Transaction Model Boundaries)
- **FR-15 (List/search/sort/paginate + folders + delete)**: Fully covered by AD-6 (Entity Relationship Model), AD-9 (API Endpoint Structure)
- **FR-18 (Rate & volume guardrails)**: Fully covered by AD-10 (Download Abuse Protection)
- **FR-19 (Report → takedown)**: Fully covered by AD-10 (Download Abuse Protection)

### 🟡 Partially covered: FR-8, FR-9, FR-10, FR-11, FR-13, FR-14
- **FR-8 (Sender accounts)**: Account system referenced but architectural decisions focus on storage/chunking. Account management architecture not detailed.
- **FR-9 (Server-enforced quota)**: Referenced in AD-9 (API Endpoint Structure) but enforcement architecture, quota model, and limit checking not specified.
- **FR-10 (Link expiry)**: Mentioned as functional requirement but expiry mechanism, TTL management, and cleanup processes not architecturally defined.
- **FR-11 (Download-count cap)**: Referenced in AD-10 (Download Abuse Protection) but counting mechanism, enforcement point, and atomic counter design not specified.
- **FR-13 (Egress passthrough Studio tier)**: Payment integration covered by AD-11 (Payment Integration Architecture) but only for Ko-fi webhook. Metering infrastructure, per-GB billing, and overage tracking architecture missing.
- **FR-14 (Download tracking/receipt)**: Referenced in AD-6 (Entity Relationship Model) and AD-10 (Download Abuse Protection) but tracking metadata model and query patterns not detailed.

### 🔴 Missing coverage: FR-4, FR-5, FR-6, FR-7, FR-12, FR-20, FR-21, FR-22

**Critical Gaps (Client-Side Encryption):**
- **FR-4 (Encrypt-before-upload with key in fragment)**: No architectural decisions for client-side encryption implementation, WASM crypto integration, or key management
- **FR-5 (Independent per-chunk AEAD)**: No architecture for per-chunk encryption, nonce generation, or AEAD structure
- **FR-6 (Verifiable zero-knowledge)**: No architecture for verification UI, network inspection capabilities, or trust mechanism

**Account & Access Control Gaps:**
- **FR-7 (Recipient-never-authenticates)**: No architectural specification for public delivery endpoints, authentication bypass, or access control
- **FR-12 (Password protection)**: No architecture for password hashing, verification flow, or interaction with fragment key (noted as Open Question 3 in PRD)

**Ad System Gaps:**
- **FR-20 (Plan-scoped ad placement)**: No architecture for ad injection, network integration, or curated allowlist
- **FR-21 (Clean trial allowance)**: No architecture for trial delivery tracking, allowance counting, or transition logic
- **FR-22 (Anti-adblock recovery)**: No architecture for adblock detection, recovery mechanisms, or soft-wall implementation

## Critical Gaps

### 1. **Client-Side Encryption Architecture Missing (CRITICAL)**
**Impact:** Core value proposition "IP-safe by construction" has no architectural foundation
- No WASM crypto integration pattern specified
- No key generation/distribution architecture
- No per-chunk AEAD structure design
- No specification for browser crypto API usage
- **Risk:** Implementation may be inconsistent across platforms or have security vulnerabilities

### 2. **Zero-Knowledge Verification Architecture Missing (HIGH)**
**Impact:** Trust mechanism is architectural undefined
- No architecture for network inspection UI/tools
- No specification for "verify it yourself" documentation system
- No design for demonstrating ciphertext-only traffic
- **Risk:** Trust claims cannot be substantiated architecturally

### 3. **Ad System Architecture Completely Missing (HIGH)**
**Impact:** Free-tier economics depend on ads but no architectural foundation
- No ad injection pipeline or component architecture
- No network integration patterns (Adsterra/PopAds/Monetag)
- No plan-scoped ad display decision architecture
- No clean trial tracking system
- No anti-adblock recovery mechanism
- **Risk:** Ad integration may be implemented inconsistently or break recipient experience

### 4. **Access Control Architecture Partially Defined (MEDIUM)**
**Impact:** Recipient access and security controls incomplete
- FR-7 (recipient-no-auth) needs architectural specification
- FR-12 (password protection) needs password hashing/verification architecture
- Open Question 3 (password vs fragment key interaction) unresolved
- **Risk:** Access controls may be implemented inconsistently or have security vulnerabilities

### 5. **Quota Enforcement Architecture Undefined (MEDIUM)**
**Impact:** Cost control depends on quota enforcement but architecture missing
- No quota model architecture (Plan limits, enforcement points)
- No server-side quota checking pattern
- No download-count cap enforcement architecture
- **Risk:** Quota enforcement may be bypassable or inconsistent

### 6. **Payment Integration Architecture Incomplete (MEDIUM)**
**Impact:** Revenue generation architecture underspecified
- AD-11 covers Ko-fi webhook but architectural scope limited
- No VietQR integration architecture (primary payment rail per PRD assumption)
- No egress passthrough metering infrastructure
- No per-GB billing architecture for Studio tier
- **Risk:** Payment system may not support required VN payment methods or metering

## Recommendations

### Immediate Architecture Additions Required
1. **Client-Side Encryption AD**: Define WASM crypto integration, key generation, per-chunk AEAD structure
2. **Zero-Knowledge Verification AD**: Define verification UI, network inspection architecture
3. **Ad System AD**: Define ad injection pipeline, network integration, plan-scoped display
4. **Access Control AD**: Define public delivery endpoints, password protection architecture
5. **Quota Enforcement AD**: Define quota model, enforcement points, download counting

### Clarification Needed
- **Open Question 3**: Password vs fragment key interaction needs architectural resolution
- **VietQR Integration**: Needs architectural decision (primary payment rail per PRD)
- **Trial Allowance Architecture**: Clean trial tracking needs specification

### Architecture Spine Bindings Update
Current spine shows `binds: [FR-1, FR-2, FR-3, FR-4, FR-5, FR-13, FR-18, FR-19, FR-20]` but FR-4, FR-5, FR-20 have no architectural coverage. This binding list should be updated to reflect actual coverage.