---
title: ZapDrop
status: final
created: 2026-07-19
updated: 2026-07-19
finalized: 2026-07-19
---

# PRD: ZapDrop
*Formerly TeleCloud. Renamed 2026-07-19 to decouple from Telegram branding after the storage backend pivot.*

## 0. Document Purpose

This PRD is for the product owner (Cuong, solo dev) and downstream UX / architecture / epic-authoring work. It supersedes the original one-page brainstorm PRD after a research-driven pivot (party-mode session, 2026-07-19, six web-research missions on market, rivals, Telegram ToS, VN market, bandwidth cost, and competitor tier economics — full evidence in `addendum.md`). It builds on, and in places overrides, the existing codebase specs (`_bmad-output/specs/spec-telecloud/SPEC.md`, `_bmad-output/architecture/architecture-TeleCloud-2026-07-18/ARCHITECTURE-SPINE.md`). Vocabulary is Glossary-anchored (§3); features grouped with globally numbered FRs; assumptions tagged inline `[ASSUMPTION]` and indexed in §9. Cost/economics detail and research sources live in `addendum.md`, not here.

**Two headline changes from the prior draft, both evidence-forced:**
1. **Storage backend moves off Telegram to owned storage** (dedicated disk + cold overflow). Telegram is both more expensive (upload-push = 2× egress) *and* carries existential ToS/ban risk for a paid product.
2. **The wedge shifts** from "Telegram without the 2 GB ceiling" (a saturated space — gigafile.nu does 300 GB free, Fsend.vn does 10 GB in Vietnamese) to **workflow-grade delivery for creative professionals**: encrypted (IP-safe), branded, tracked, with bandwidth-aware pricing.

## 1. Vision

Creative professionals — video editors, wedding/event photographers, 3D/motion artists — deliver large files to clients constantly, and every tool they use is wrong in a specific way. Google Drive makes the recipient log into Google and "request access." WeTransfer free caps at 3 GB, expires in days, and in 2025 changed its terms to let itself train AI on uploaded files — a direct threat to the professional's IP. MASV works but bills per-GB-downloaded, producing surprise invoices ($275 for a 100 GB file the client re-downloaded). Local Vietnamese option Fsend.vn is a raw send-a-file box with no delivery workflow.

**ZapDrop is a delivery tool built for the people whose job is sending big files to clients.** Upload a file of any size (transparently chunked so there is no ceiling), and hand the client a single link — no account, no "request access." The file is encrypted on your device before it is stored, so **no one operating the service, and no third party, can read it** — the honest answer to the AI-training fear. The delivery page carries your brand, you can see when the client downloaded, set how long the link lives, protect it with a password, and — where it matters — charge for the download and get paid through local rails (VietQR/MoMo), not a US-only processor.

Underneath, ZapDrop runs on **storage it owns** — a dedicated server with disk, overflowing to cheap cold storage — not on a third party's platform that can delete the files or ban the account. That choice is what lets ZapDrop make a promise it can keep: the file is there when the client clicks. The unit economics work (commodity bandwidth is a fixed cost, not a per-byte tax), leaving healthy margin. The open risks are honest and named: whether enough professionals will pay, and how they find the product at all against incumbents with 80M users.

## 2. Target User

### 2.1 Jobs To Be Done

- **Deliver a finished large file to a client without friction.** "Here's your video / photo set / build — one link, you click, you download. No Google login, no app."
- **Protect my IP.** "I don't want the transfer service reading, keeping, or training AI on my client's footage." *(Sharpened by the 2025 WeTransfer-AI backlash.)*
- **Look professional in the handoff.** Branded delivery page, my logo, my message — not a generic "someone sent you a file" screen.
- **Know it arrived.** See whether/when the client downloaded; get a receipt.
- **Control the link.** Set expiry, require a password, cap how many times it can be downloaded.
- **Get paid for a deliverable** through a payment method that actually works in Vietnam.
- **(Builder's own job)** Run this profitably as a solo dev: storage/bandwidth cost well below what customers pay, on infrastructure I control, without depending on a platform that can cut me off.

### 2.2 Non-Users (v1)

- **Consumers doing one-off "send a big file once" transfers.** They will use the free tier and never pay; fine as marketing, never the paying target.
- **People wanting cloud sync / a mounted drive.** ZapDrop delivers, it does not sync.
- **Anonymous file-dumping / public-link mass distribution.** A link downloaded 10,000 times is a cost bomb and an abuse vector (see FR-18, FR-19); the product is one-to-few professional delivery, not one-to-the-world hosting.
- **Teams needing SSO / shared workspaces / admin consoles** — deferred beyond v1.

### 2.3 Key User Journeys

- **UJ-1. Huy delivers a 6 GB master to a client, branded, and sees them download it.**
  - **Persona + context:** Huy, freelance video editor in Da Nang. Delivering a finished cut to a client abroad; WeTransfer free won't take 6 GB and he doesn't trust its AI terms.
  - **Entry state:** Logged in (his account), on the ZapDrop web app.
  - **Path:** Uploads the 6 GB file (chunked, encrypted on his machine, live progress) → sets a 14-day expiry, adds his studio logo + a note → gets a link → pastes it to the client.
  - **Climax:** The client opens a branded page, downloads without any login; Huy sees "downloaded 2h ago." Realizes FR-1, FR-4, FR-10, FR-11, FR-12, FR-14.
  - **Resolution:** Deliverable done, tracked, IP never readable by the service. **Edge case:** connection drops at chunk 8 → resumes from chunk 8.

- **UJ-2. Mai (the client) downloads on her phone, no app, no account.**
  - **Persona + context:** Mai, non-technical client, won't install anything.
  - **Entry state:** Unauthenticated, opens the link in her phone browser.
  - **Path:** Branded page → Download → chunks stream, reassemble, hash-verify, decrypt with the key from the link fragment → file saves.
  - **Climax:** Full 6 GB lands intact; she never logged in. Realizes FR-3, FR-7.
  - **Resolution:** Done. **Edge case:** link expired → clear "this delivery has expired" page (410).

- **UJ-3. Trang runs a wedding studio and charges for the gallery.**
  - **Persona + context:** Trang delivers ~200 GB of RAW photos per event and wants payment before release, plus a branded, password-protected handoff.
  - **Entry state:** Logged in, VietQR/MoMo connected.
  - **Path:** Uploads the set → sets a price, a password, her brand → sends the link.
  - **Climax:** Client pays via VietQR, enters password, downloads; Trang sees payment + download status. Realizes FR-11, FR-12, FR-13, FR-14.
  - **Resolution:** Paid, delivered, tracked. **Edge case:** payment abandoned → download stays locked.

- **UJ-4. Cuong (operator) serves 100 paying studios without the cost spiraling.**
  - **Persona + context:** The builder. 100 customers moving ~500 GB/month each.
  - **Entry state:** Owned dedicated server + cold overflow; per-tier download caps and expiry in force.
  - **Path:** Traffic flows from owned disk; links expire; heavy tiers pass egress through.
  - **Climax:** ~$115/month infrastructure against ~$800–1,000 revenue — margin holds. Realizes FR-15, FR-18, FR-19, plus the pricing model in §4.4.
  - **Resolution:** Sustainable. **Edge case:** a link gets mass-downloaded → download cap + expiry bound the egress; abuse → takedown.

## 3. Glossary

- **Delivery** — A single share created by a sender: one file (v1) plus its settings (expiry, password, branding, price, download cap). Identified by a **Share ID**. Realized as a **Manifest** + its **Chunks**. (Replaces legacy "TempShare" / "Transfer".)
- **Chunk** — One encrypted slice of a file, stored as one object in the **Storage backend**. A file is split into an ordered set of chunks.
- **Manifest** — The encrypted record of a Delivery's file: original name, MIME, total size, whole-file hash, ordered chunk list (index, size, per-chunk hash, storage reference), crypto parameters. Contains **no key**.
- **Share ID** — Public identifier in a Share Link that resolves to a Manifest. Carries no secret.
- **Decryption Key** — AES-256-GCM key for a Delivery. Lives **only** in the Share Link's **URL fragment** (`#...`), never sent to any server.
- **Share Link** — `.../d/{Share ID}#{Decryption Key}`. Share ID reaches the server; the fragment (key) does not.
- **Sender** — Authenticated party who creates a Delivery. Has a **Plan**.
- **Recipient** — Party who opens a Share Link to download. **Never authenticates.**
- **Plan** — Sender subscription level governing quota: **Free**, **Pro**, **Studio**. (Replaces the brainstorm's Guest/Verified/Paid identity tiers; a Sender is always a signed-in account, gated by Plan.)
- **Quota** — Per-Plan limits: max file size, monthly volume, max expiry, **download-count cap per link**, concurrency.
- **Storage backend** — Owned infrastructure: a **primary dedicated server with disk**, overflowing to **cold object storage** (e.g. Backblaze B2) when disk fills. **Not Telegram.**
- **Download cap** — Max number of times a single Share Link may be downloaded before it locks — the primary bandwidth-cost control.
- **Egress passthrough** — At the top Plan, download volume beyond an included allowance is billed per-GB to the sender (MASV-style), so cost scales with revenue.
- **Delivery page** — The recipient-facing page for a Delivery (branded for paid senders; ad-supported for Free senders).
- **Clean delivery** — A Delivery whose recipient page shows no ads. All Pro/Studio deliveries, plus a new account's trial allowance.
- **Ephemeral** — Default posture: Deliveries expire and their storage is reclaimed.

## 4. Features

### 4.1 Large-file chunked delivery

**Description:** The sender uploads a file of any size. The client encrypts it (§4.2), splits the ciphertext into ordered Chunks, and uploads them to the owned Storage backend in parallel (Plan-dependent concurrency). Per-chunk and whole-file SHA-256 are computed; the Delivery commits only when every Chunk is stored, then the encrypted Manifest is written and the Share Link produced. Uploads resume at chunk granularity after a dropped connection. On download, chunks stream back, are hash-verified, reassembled, and decrypted. Realizes UJ-1, UJ-2.

**Functional Requirements:**

#### FR-1: Split-and-store any-size file
A Sender can upload a file of any size, stored as an ordered set of Chunks on owned storage. Realizes UJ-1.
- **Consequences:** Files well beyond 2 GB succeed and produce a working Share Link; Manifest records chunk order, sizes, per-chunk + whole-file SHA-256, and storage references. Default chunk target [ASSUMPTION: 256–512 MB, tunable; recalibrate against first month of production data, if off by >20% adjust].

#### FR-2: Parallel upload with chunk-level resume
Upload runs multiple Chunks concurrently and survives connection loss without restarting.
- **Consequences:** Concurrency capped per Plan; after a drop, only missing Chunks re-upload; a Delivery is downloadable only after commit.

#### FR-3: Download reassembly with integrity verification
A Recipient receives the original file reassembled and verified byte-for-byte.
- **Consequences:** Each Chunk's SHA-256 verified on arrival (failing chunk re-fetched, not whole file); whole-file SHA-256 verified after reassembly; mismatch surfaces an error, never a corrupt file.

**Feature-specific NFRs:** Throughput on Vietnamese networks is a primary concern; chunk-size × concurrency defaults must be benchmarked (Open Question 6).

### 4.2 Client-side encryption (IP-safe by construction)

**Description:** Before any Chunk leaves the device, the file is encrypted with a freshly generated AES-256-GCM key; each Chunk is an independent AEAD unit (unique nonce; AAD binds `manifest_id ‖ chunk_index`). The Manifest is encrypted with the same key. The key lives only in the Share Link fragment and is never transmitted to any server (RFC 3986 — fragments are not sent in requests). The service therefore only ever handles ciphertext. This is the honest, verifiable answer to the professional's IP fear ("we cannot read, keep, or train on your files — by design"), positioned as **trust**, not as "military-grade privacy" marketing. Realizes UJ-1, UJ-2.

**Functional Requirements:**

#### FR-4: Encrypt-before-upload with key in fragment
File and Manifest are encrypted client-side; the key exists only in the Share Link fragment.
- **Consequences:** No request to the server contains plaintext file bytes or the key (network-verifiable); the stored Manifest is ciphertext; a link with the fragment stripped cannot decrypt.

#### FR-5: Independent per-chunk AEAD
Each Chunk is encrypted/authenticated independently for parallel transfer, resume, and integrity.
- **Consequences:** Unique nonce per chunk; AAD binds manifest id + index; a swapped/reindexed chunk fails authentication.

#### FR-6: Verifiable zero-knowledge (trust without open source)
A user can verify, on their own network, that only ciphertext leaves the client and the key never does.
- **Consequences:** In-app inspector or documented procedure shows ciphertext-only request bodies and the key absent from all requests. On the web app, the client crypto runs in-browser and is inspectable via DevTools. [ASSUMPTION: web client crypto is inspectable-by-default; a guided "verify it yourself" doc is the MVP artifact.]

**Notes:** Published crypto spec + open-source verifier, crack-me/bounty, and paid audit are post-MVP trust layers (§6.2). Because the client is web (WASM/JS), it is inherently inspectable — server (and any storage internals) stay closed.

### 4.3 Accounts & plans

**Description:** Recipients never authenticate. Senders are always signed-in accounts on a **Plan** (Free / Pro / Studio) that sets Quota. Quota is **enforced server-side**. Login uses [ASSUMPTION: Google OAuth, reusing existing wiring]. Realizes UJ-1, UJ-3, UJ-4.

**Functional Requirements:**

#### FR-7: Recipient-never-authenticates
A Recipient opens a Share Link and downloads with no account or login.
- **Consequences:** Delivery page and its supporting endpoints work with no auth cookie; no step prompts sign-in.

#### FR-8: Sender accounts
A Sender signs in and operates under a Plan; Free by default, upgradeable to Pro/Studio.
- **Consequences:** New account defaults to Free; Plan is a stored field a payment event updates (FR-13).

#### FR-9: Server-enforced quota
Per-Plan limits (file size, monthly volume, max expiry, download cap, concurrency) are enforced server-side.
- **Consequences:** Exceeding a limit returns 403/429 with an upgrade nudge; a patched client cannot exceed limits; Plan limits are a single config table, not scattered in handlers. Starting matrix in `addendum.md`.

### 4.4 Bandwidth-aware pricing & cost control

**Description:** The dominant marginal cost is **egress per download**, which scales with *downloads-per-link*, not file size. Pricing and product controls are built around that fact. Three levers keep flat-fee tiers solvent: **short default expiry**, a **download-count cap per link**, and **egress passthrough at the Studio tier** (volume beyond an included allowance billed per-GB). This is the difference between a sustainable business and WeTransfer/TransferNow's exposure (a 1 TB link downloaded 50× costs them 50 TB while the customer pays flat). Realizes UJ-3, UJ-4.

**Functional Requirements:**

#### FR-10: Link expiry
A Sender sets an expiration; expired Deliveries are inaccessible and their storage reclaimed.
- **Consequences:** Expired access returns 410; max expiry bounded by Plan; on expiry, Chunks + Manifest are deleted from storage (ephemeral + cost control).

#### FR-11: Download-count cap
Each Delivery has a maximum download count; once reached, the link locks.
- **Consequences:** Downloads beyond the cap return a locked/410 state; cap default and ceiling are Plan-bound; the sender can see the count (FR-14). This is the primary egress-cost guardrail.

#### FR-12: Paid delivery via local rails
A Sender can set a price; the Recipient must pay before download unlocks. **Payment via VietQR/MoMo/ZaloPay — not Stripe** (Stripe is unavailable in Vietnam).
- **Consequences:** Unpaid access is locked until a payment webhook confirms; confirmed payment unlocks; payment status visible on the Delivery. [ASSUMPTION: VietQR/bank-transfer first as the lowest-friction solo-dev rail; MoMo/ZaloPay follow.]

#### FR-13: Egress passthrough (Studio tier)
At the Studio Plan, download volume beyond an included monthly allowance is billed per-GB to the sender.
- **Consequences:** Sender sees included allowance + overage rate; overage charged per confirmed GB served; keeps heavy-usage cost aligned with revenue. [ASSUMPTION: Studio-only; Free/Pro rely on expiry + download cap instead.]

**Feature-specific NFRs:** The tier matrix (prices, allowances, caps) is in `addendum.md` and is configuration, tunable without code changes.

### 4.5 Branded, tracked delivery

**Description:** A Sender brands the Delivery page and can see delivery status. This is core to the professional-handoff wedge, not decoration. Realizes UJ-1, UJ-3.

#### FR-11: Password protection
A Sender can require a password before download. **Server-side gate**: the server withholds Manifest resolution until the correct password is provided; the fragment key alone is useless without server cooperation.
- **Consequences:** Correct password → server serves Manifest + Chunks, then client decrypts with the fragment key. Wrong password → 401. This is the same mechanism as paid-delivery gating (FR-12).

#### FR-12: Branded delivery page
A Sender customizes the Delivery page with logo, background, and message.
- **Consequences:** Page renders per-Delivery branding; not a global setting.

#### FR-14: Download tracking / receipt
A Sender can see whether and when a Delivery was downloaded, and how many times.
- **Consequences:** Delivery detail shows download count + last-download time; drives the download-cap display (FR-11). [ASSUMPTION: count + timestamp in MVP; per-recipient identity not tracked, consistent with recipient-no-auth.]

### 4.6 File management

**Description:** Signed-in Senders manage their Deliveries and folders — list (search/sort/paginate), get metadata, organize, delete. Carries over from legacy CAP-1..CAP-7, scoped to the current user.

#### FR-15: List / search / sort / paginate + folders + delete
A Sender manages their own Deliveries and folders.
- **Consequences:** Results scoped to current user; invalid sort → 400; non-empty folder delete → 409; duplicate folder path → 409; deleting a Delivery removes Chunks + Manifest from storage and the metadata record; 204/404/403 as appropriate. Folder model: flat hierarchy (no nesting beyond root level) in MVP; folder rename allowed via move operation (new path); Deliveries can be moved between folders; folder paths are unique per user.

### 4.7 Abuse control & takedown

**Description:** Owned storage removes the ToS/ban dependency but keeps the abuse problem: an E2E delivery tool that the operator cannot read is exactly what killed Firefox Send (a ransomware/phishing haven with no report button). Because content is encrypted, moderation is metadata + identity + rate + report-driven, and a report locks/removes a Delivery. Realizes UJ-4.

#### FR-18: Rate & volume guardrails
Server-enforced rate limits, per-Plan monthly volume ceilings, and download caps bound abuse and cost.
- **Consequences:** Abnormal rates throttle/refuse; volume beyond Plan is refused or (Studio) billed as overage (FR-13); metadata-only logging (who/when/size/share-id/ciphertext-hash), never content.

#### FR-19: Report → takedown
Anyone with a link can report a Delivery; a valid report locks/removes it.
- **Consequences:** Report against a Share ID deletes its Chunks + Manifest without decryption; the link then returns a 410/unavailable state; **a report button ships in v1** (Firefox Send's absence of one was fatal).

### 4.8 Free-tier ad funding

**Description:** The Free tier's marginal cost (bandwidth) is offset by ads, but ads are contained so they never touch the paying product or a paying customer's clients. Ads appear **only on the recipient download page of deliveries sent by a Free sender**. Cleanliness follows the **sender's Plan**, not the recipient: a Pro/Studio sender's recipients see a **clean** page — studios pay in part to give their own clients an ad-free handoff, which makes "clean for your clients" an explicit upgrade reason. New accounts get a small number of **clean trial deliveries** to feel the professional experience before ads apply. The economics are honest: at Vietnamese CPMs ($0.10–1), ads **reduce** the Free tier's loss, they do not make it profitable — so the primary cost control is capping Free-tier egress (FR-10, FR-16), with ads second. Realizes UJ-4 (operator sustainability).

**Functional Requirements:**

#### FR-20: Plan-scoped ad placement
Ads render only on the recipient download page, and only when the Delivery's sender is on the Free Plan (outside any clean-trial allowance).
- **Consequences:** Sender-side (upload/management) pages never show ads. A Pro/Studio-sent Delivery's recipient page is ad-free. A Free-sent Delivery's recipient page shows ads (interstitial countdown format; gambling permitted but not egregiously dirty content — a curated-network allowlist, not raw open exchange). [ASSUMPTION: ad-content standards enforced via network choice + category blocks.]

#### FR-21: Clean trial allowance
A new account's first deliveries are ad-free regardless of Plan, then revert to ad-supported Free if not upgraded.
- **Consequences:** [ASSUMPTION: 3–5 clean deliveries per new account; tune against conversion data — if conversion <5% after 5 deliveries, increase allowance; if >15% after 3, decrease]. After the allowance, Free-sent deliveries become ad-supported (FR-20); this protects the free→paid funnel (a pro trialing Free must not show clients ads).

#### FR-22: Anti-adblock recovery (not hard-block)
Ad revenue is protected against adblockers by ad-recovery / soft-wall, not by blocking file access.
- **Consequences:** Use Ad-Shield-style recovery (serve ads via own subdomain to evade filter lists) and/or a soft-wall ("please allow ads to keep the free tier running" + retry). **Downloads are NOT hard-blocked on adblock detection** — that loses the uBlock arms race and false-blocks legitimate users (Brave, Firefox privacy mode, DNS-level filtering) from files they may download. [ASSUMPTION: Ad-Shield is a paid third-party dependency; evaluate cost vs a self-hosted soft-wall.]

**Feature-specific NFRs:** Break-even requires Free-tier egress to stay capped (see FR-10, FR-11); an uncapped Free download defeats the ad model. AdSense is unusable (its policies bar file-sharing sites, and E2E means no content control to satisfy them) — monetization uses download-niche networks (Adsterra/PopAds/Monetag-class) under category filters.

## 5. Non-Goals (Explicit)

- **Technology stack change.** Built on existing .NET 10 (Blazor + FastEndpoints + WolverineFx + EF Core + PostgreSQL) web app with RizzyUI components. The pivot is storage-only; UI framework, language, and architecture stay unchanged.
- **Not a Telegram front-end / not built on Telegram storage.** Owned storage only. (Legacy Telegram code archived at `codes/TeleCloud-telegram-archive/`, not deleted from the running repo until owned storage ships.)
- **Not competing on "biggest free" / "most unlimited."** That space is saturated (gigafile.nu 300 GB free, Smash unlimited, Fsend.vn 10 GB VN). Free tier is a funnel, not the pitch.
- **Not sync / not a mounted drive.**
- **Not anonymous mass public hosting.** One-to-few professional delivery; download caps + expiry + report enforce this.
- **No "military-grade / anonymous" marketing.** Trust is demonstrated (verifiable zero-knowledge), not asserted.
- **No open-sourcing the whole app.** Server closed (protects paid logic + storage internals); web client is inherently inspectable, which is the trust mechanism.
- **No Stripe.** VN payment rails only.
- **No permanent-storage guarantee marketed as such.** Ephemeral by default; retention is bounded (retention is the cost-accumulation trap — §addendum).
- **No native mobile app in v1** (web only).
- **No teams / SSO / admin console / soft-delete / thumbnails** in v1.

## 6. MVP Scope

### 6.1 In Scope
- **Owned Storage backend:** primary dedicated disk, with cold overflow when disk fills (FR-1, FR-3, plus §addendum architecture).
- **Chunked upload/download** >2 GB with SHA-256 verification, parallel transfer, chunk-level resume, commit-on-complete (FR-1, FR-2, FR-3).
- **Client-side encrypt-before-upload**, per-chunk AEAD, encrypted Manifest, key in fragment (FR-4, FR-5); verifiable-zero-knowledge doc/inspector (FR-6).
- **Accounts + Plans** (Free/Pro/Studio) with server-enforced quota (FR-7, FR-8, FR-9).
- **Bandwidth-aware controls:** link expiry (FR-10) + **download-count cap** (FR-11). These two are non-negotiable MVP cost guardrails.
- **Branded delivery page (FR-12) + download tracking (FR-14) + password (FR-11).**
- **Paid delivery via VietQR** (FR-13) — the lowest-friction VN rail first.
- **File management** (FR-15).
- **Report button → takedown (FR-19)** + rate/volume guardrails (FR-18). Ships v1 (Firefox Send lesson).
- **Free-tier ads** on Free-sent recipient pages (FR-20), clean trial allowance (FR-21), anti-adblock recovery/soft-wall (FR-22). Contained so paying senders and their recipients stay ad-free.

### 6.2 Out of Scope for MVP
- **Egress passthrough / Studio metered overage (FR-17)** — needs metering infra; Free/Pro survive on expiry + download cap first. `[NOTE FOR PM]` add once Studio demand is real.
- **MoMo / ZaloPay** beyond VietQR — add after VietQR proves the paid flow.
- **Cold-overflow tier (Backblaze)** may be deferred until primary disk actually fills — but the architecture must not preclude it. `[NOTE FOR PM]` cheap to design in, expensive to retrofit.
- **Published crypto spec + open verifier, crack-me/bounty, third-party audit** — post-MVP trust layers.
- **Stream-as-you-go download, folder-send (directory structure), multi-file per Delivery** — v1 Delivery = one file.
- **Native mobile, desktop/CLI, LAN P2P fast-path** (LAN P2P needs native — deferred with mobile).
- **Teams / SSO / admin / soft-delete / thumbnails.**

## 7. Success Metrics

**Primary**
- **SM-1**: **Paid conversion** — % of active senders on Pro/Studio. This is the survival metric; the whole pivot rests on professionals paying. Validates FR-8, FR-13.
- **SM-2**: **Gross margin per paying customer** — revenue minus attributed storage+egress. Target ≥ [ASSUMPTION: 80% per the ~85% modeled in §addendum; recalibrate against first quarter real data — if sustained <70%, audit unit economics]. Validates FR-11, FR-13, FR-18.
- **SM-3**: **Repeat delivery rate** — paying users creating deliveries in consecutive months (proves it's a workflow tool, not one-off). Validates the workflow-wedge thesis.

**Secondary**
- **SM-4**: **Large-file delivery success rate** — ≥ [ASSUMPTION: 95% of >2 GB uploads reach commit (resume counts as success); recalibrate monthly — if <90% for 2 consecutive months, investigate chunk size/concurrency defaults]. Validates FR-2, FR-3.
- **SM-5**: **Avg downloads-per-link** — the cost driver; watched to keep flat-fee tiers solvent. Validates FR-11.

- **SM-6**: **Free-tier net cost** — ad revenue minus Free-tier egress cost. Target ≤ 0 net drain (ads cover bandwidth). Validates FR-20, FR-22 and the caps in FR-10/FR-11.

**Counter-metrics (do not optimize)**
- **SM-C1**: **Free-tier volume / signups.** Do *not* maximize — free one-off users are cost, not revenue; distribution success is measured by *paid* conversion (SM-1), not raw signups.
- **SM-C3**: **Ad density / revenue per free download.** Do *not* maximize — pushing more/dirtier ads poisons the free→paid funnel (a Free-trialing pro's clients see the ads). Bounded by FR-21 and category filters.
- **SM-C2**: **Feature breadth.** Do *not* let the tool sprawl back toward a generic WeTransfer clone; every feature must serve the professional-delivery wedge.

## 8. Open Questions

1. ~~**Product name**~~ → **Resolved: ZapDrop** (2026-07-19). Formerly TeleCloud; renamed to decouple from Telegram.
2. **Does the paying market exist as modeled?** — The single biggest unverified assumption. Interview **5 real VN/SEA studios/editors**: how often they deliver large files, with what, the worst pain, and whether they'd pay ~99–299k VND/mo. `[NOTE FOR PM]` this gates the whole build — data narrowed the assumption, it did not confirm it. *Not blocking the build; validated post-launch.*
3. ~~**Password (FR-11) vs fragment key**~~ → **Resolved: server-side gate.** Password is checked server-side before Manifest resolution. The fragment key alone is useless without server cooperation — the recipient needs both password (to reach the Manifest) and the fragment key (to decrypt). This avoids crypto complexity (no key derivation mixing) and keeps the server as the gate for access control, which also solves the paid-delivery flow (Q4).
4. ~~**Paid delivery (FR-12) vs fragment key**~~ → **Resolved: server withholds Manifest until payment.** Server withholds Manifest/Share-ID resolution until the payment webhook confirms, so the key alone is useless without server cooperation. Same pattern as password gate (Q3) — consistent, one mechanism.
5. **Distribution** — how do professionals discover the product against incumbents with 80M users? (Not a technical question; the other unsolved half besides Open Question 2.) *Not blocking the build; revisit post-launch.*
6. ~~**Chunk-size × concurrency defaults**~~ → **Resolved: benchmark before locking.** Will tune against VN network conditions during implementation.
7. ~~**Cold-overflow trigger**~~ → **Resolved: design in, implement when primary disk approaches capacity.** Architecture must not preclude it; implementation deferred until actually needed.

## 9. Assumptions Index

- **§4.1 / FR-1** — Chunk target 256–512 MB, tunable.
- **§4.2 / FR-6** — Web client crypto is inspectable-by-default; MVP trust artifact is a "verify it yourself" doc + inspector.
- **§4.3 / FR-8** — Sign-in reuses existing Google OAuth wiring.
- **§4.4 / FR-12** — VietQR/bank-transfer first as the solo-dev rail; MoMo/ZaloPay later.
- **§4.4 / FR-13** — Egress passthrough is Studio-only; Free/Pro use expiry + download cap.
- **§4.5 / FR-11** — Password is a server-side access gate; server withholds Manifest until password verified. Fragment key alone useless without server cooperation (confirmed, OQ-3 resolved).
- **§4.5 / FR-14** — Download tracking = count + timestamp, not per-recipient identity.
- **§4.8 / FR-20** — Ad-content standards (gambling-OK-but-not-egregious) enforced via curated network allowlist + category blocks, not open exchange.
- **§4.8 / FR-21** — Clean trial allowance of 3–5 deliveries per new account (tune against conversion data).
- **§4.8 / FR-22** — Ad-Shield is a paid third-party dependency; evaluate vs a self-hosted soft-wall.
- **§6 / storage** — Cold overflow (Backblaze) may be deferred until disk fills but must be designed in.
- **§7 / SM-2, SM-4** — 80% margin and 95% success targets are placeholders to calibrate against real data.
