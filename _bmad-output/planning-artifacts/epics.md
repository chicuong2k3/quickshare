---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - "_bmad-output/planning-artifacts/prds/prd-TeleCloud-2026-07-19/prd.md"
  - "_bmad-output/architecture/architecture-TeleCloud-owned-storage-2026-07-19/ARCHITECTURE-SPINE.md"
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md"
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/EXPERIENCE.md"
---

# ZapDrop - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for ZapDrop (formerly TeleCloud), decomposing the requirements from the PRD, UX design contract, and Architecture spine into implementable stories.

## Requirements Inventory

### Functional Requirements

FR-1: Split-and-store any-size file — chunk large files into ordered chunks, store on owned disk storage with SHA-256 verification
FR-2: Parallel upload with chunk-level resume — concurrent chunk upload, survive connection loss without restart
FR-3: Download reassembly with integrity — per-chunk + whole-file SHA-256 verification, corrupt chunk re-fetch
FR-4: Encrypt-before-upload with key in fragment — AES-256-GCM, key in URL fragment (#), never transmitted to server
FR-5: Independent per-chunk AEAD — unique nonce per chunk, AAD binds manifest_id + chunk_index, anti-swap/reorder
FR-6: Verifiable zero-knowledge — user can verify ciphertext-only network traffic via browser DevTools inspection
FR-7: Recipient-never-authenticates — download without account, login, or app install
FR-8: Sender accounts with plans — Google OAuth sign-in, Free/Pro/Studio plan tiers
FR-9: Server-enforced quota — per-plan limits (file size, monthly volume, expiry, concurrency) enforced server-side
FR-10: Link expiry — sender sets expiration, expired deliveries return 410, storage reclaimed on expiry
FR-11: Download-count cap — max downloads per link, Plan-bound (Free: 5, Pro: 50, Studio: configurable)
FR-12: Paid delivery via Ko-fi — sender sets price, recipient pays via Ko-fi before download unlocks
FR-13: Egress passthrough (Studio tier, post-MVP) — overage billed per-GB beyond monthly allowance
FR-14: Password protection — server-side access gate, withholds Manifest until password verified
FR-15: Branded delivery page — sender logo, message, background on recipient page (Pro/Studio only)
FR-16: Download tracking — count + last-download timestamp visible to sender on dashboard
FR-17: File management — list/search/sort/paginate deliveries, flat folder hierarchy, delete with cascade
FR-18: Rate & volume guardrails — server-enforced rate limits, per-plan monthly volume ceilings
FR-19: Report → takedown — anyone can report a delivery link, immediate server-side chunk+manifest deletion
FR-20: Plan-scoped ad placement — interstitial ads only on Free-sender recipient pages; Pro/Studio always clean
FR-21: Clean trial allowance — 3-5 ad-free deliveries for new accounts before ad gate activates
FR-22: Anti-adblock recovery — soft-wall (ask + retry), never hard-block file downloads

### NonFunctional Requirements

NFR-1: Vietnamese network throughput — chunk-size × concurrency defaults benchmarked against VN ISP conditions
NFR-2: Tier matrix configurability — all plan limits (sizes, caps, expiry ceilings) in configuration, not hardcoded
NFR-3: Ad-content standards — curated network allowlist + category blocks, gambling-OK but not egregious
NFR-4: Storage cost control — expiry + download-cap ensure storage/egress cost stays below revenue
NFR-5: Desktop-first responsive — 3 breakpoints (mobile < 640px, tablet 640-1024px, desktop > 1024px)
NFR-6: Accessibility — keyboard navigation, focus indicators, aria-live regions, prefers-reduced-motion respect
NFR-7: Light mode only — no dark mode support required; color tokens single-theme

### Additional Requirements (from Architecture)

AR-1: IStorageProvider interface — LocalDisk primary, BackblazeB2 cold overflow; DI-registered singleton, config-switched
AR-2: Three-phase chunked upload — POST /api/uploads/start → chunk uploads (parallel, idempotent) → POST commit (atomic)
AR-3: UploadSession entity — 24h TTL, background daily cleanup job (Hangfire or HostedService)
AR-4: Idempotent chunk operations — skip-if-exists by uploadId + chunkIndex, safe client retry
AR-5: Wolverine transaction model — individual transactions per chunk, atomic commit for Delivery + Manifest + chunk linking
AR-6: Entity model — Delivery → Manifest → Chunks cascade; UploadSession → Chunks (in-progress); DownloadTracking (IP-based)
AR-7: Manifest as encrypted bytea in PostgreSQL — co-located with Delivery metadata, ACID queries
AR-8: Clean-break migration — new Delivery/Manifest/Chunk entities via EF Core; legacy File/TempShare deprecated
AR-9: API endpoints — /api/uploads/* (chunked ops), /api/deliveries/* (CRUD), /d/{shareId} (public, no-auth)
AR-10: IP-based download tracking + rate limiting — no recipient auth, IP logs for abuse detection
AR-11: Ko-fi webhook payment verification — signature-validated, atomic Delivery unlock on confirmed payment
AR-12: LightResults Result<T> error handling — structured errors, validation failures return 400/403/413, system errors 500
AR-13: Configuration-driven storage provider — TeleCloudOptions.StorageProvider, restart to switch
AR-14: Background cleanup job — daily sweep of expired UploadSessions + orphaned chunks, batch-transactional
AR-15: Client-side encryption (WASM/JS WebCrypto) — AES-256-GCM, per-chunk AEAD, key never leaves browser
AR-16: Atomic download counter — increment per successful download, cap enforcement at API level, 410 when reached
AR-17: Egress byte tracking — Delivery.EgressBytes + User.EgressAllowance, allowance check before chunk serving
AR-18: Ad injection at page template level — User.PlanId lookup via Delivery.SenderId, invariant at template not handler
AR-19: Access control — public /d/{shareId} no-auth, password as independent server-side gate, two-factor access
AR-20: Zero-knowledge verification — in-app network inspector + DevTools guide, inspectable WASM/JS crypto
AR-21: Anti-adblock soft-wall — "please allow ads" message + retry, never hard-block, ad-recovery via subdomain optional

### UX Design Requirements

UX-DR1: Upload Drop Zone — drag-drop + click-to-browse, hover/active visual states, dashed border → solid accent on drag
UX-DR2: Upload Progress — animated bar + speed/ETA/percentage text, "Encrypting..." label, completion → link card transition
UX-DR3: Share Link Card — file icon + name + size, read-only monospace link field + copy button, expiry label
UX-DR4: Delivery Grid Card (Dashboard) — thumbnail area + file name + status badge + download count + days remaining + action icons (copy/extend/delete)
UX-DR5: Download Page (recipient) — branded sender area (Pro/Studio), file card, download button, encryption badge, report link
UX-DR6: Bottom Tab Bar (Mobile) — Upload / Deliveries / Account tabs, 56px height, fixed bottom, accent active indicator
UX-DR7: Status Badges — pill shape (radius-full), green (active) / amber (<24h) / gray (expired), xs text
UX-DR8: Notification Toast — success/error/info variants, slide-in animation, auto-dismiss (4s success, persistent error), max 3 queue
UX-DR9: Design token system — Inter + JetBrains Mono fonts, brand colors (navy #1A1A2E + accent #4F6CF7), semantic palette, spacing scale (4px–64px), shadows (sm→xl), border radius (sm→xl)
UX-DR10: Empty states — dashboard "No deliveries yet" + upload CTA illustration, download expired "Request re-send" page
UX-DR11: Loading states — skeleton cards (shimmer) for dashboard, spinner for download page, animated bar for upload
UX-DR12: Error states — upload failed (retry), network lost (auto-retry 30s → pause), expired link (410 page), invalid code (404), server error (500 toast), download interrupted (resume from last chunk)
UX-DR13: Ad interstitial (Free tier) — 3-5s countdown overlay with ad, ad panel below file card post-countdown, same-link revisit skips countdown
UX-DR14: Payment UI (VietQR) — QR code display, "Waiting for payment..." with timer, auto-advance on webhook, retry on timeout
UX-DR15: Report form — "Report this delivery" link → reason dropdown + optional message → submit confirmation → immediate takedown
UX-DR16: Password gate — single password field before file card, server-side validation, wrong → 401 re-entry
UX-DR17: Responsive layout — desktop (1200px max), tablet, mobile breakpoints; desktop grid/tablet 2-col/mobile 1-col dashboard
UX-DR18: Accessibility — keyboard-navigable (Tab/Enter/Escape), focus rings (--border-focus), aria-labels on icon buttons, aria-live for toasts, prefers-reduced-motion disables animations
UX-DR19: Microcopy — human/direct/honest tone, Vietnamese + English, no AI hype words, brief actionable text
UX-DR20: Branded delivery customization — sender logo upload (max 180px), personal message field, accent color picker (Pro/Studio)
UX-DR21: Download-count display — "X of Y downloads remaining" below download button, cap-reached → disabled button + "Download limit reached" message
UX-DR22: Trust verification — progressive disclosure 🔒 badge → expandable panel with plain-language explanation + content hash + DevTools snippet

### FR Coverage Map

FR-1: Epic 1 — Chunked file upload to owned storage
FR-2: Epic 1 — Parallel upload with chunk-level resume
FR-3: Epic 1 — Download reassembly with SHA-256 integrity
FR-4: Epic 1 — AES-256-GCM encrypt-before-upload, key in fragment
FR-5: Epic 1 — Independent per-chunk AEAD with AAD binding
FR-6: Epic 2 — Verifiable zero-knowledge (trust panel + DevTools)
FR-7: Epic 1 — Recipient-never-authenticates download endpoint
FR-8: Epic 1 — Google OAuth sender accounts with plans
FR-9: Epic 1 — Server-enforced quota per plan
FR-10: Epic 1 — Link expiry with 410 response + storage reclaim
FR-11: Epic 1 — Download-count cap per link, Plan-bound
FR-12: Epic 2 — Paid delivery via Ko-fi webhook
FR-13: Post-MVP — Egress passthrough (excluded)
FR-14: Epic 2 — Password protection (server-side gate)
FR-15: Epic 2 — Branded delivery page (logo, message, color)
FR-16: Epic 2 — Download tracking (count + timestamp)
FR-17: Epic 1 — File management CRUD (list/search/folders/delete)
FR-18: Epic 3 — Rate & volume guardrails
FR-19: Epic 3 — Report → takedown
FR-20: Epic 3 — Plan-scoped ad placement (Free-sender only)
FR-21: Epic 3 — Clean trial allowance (3-5 ad-free deliveries)
FR-22: Epic 3 — Anti-adblock soft-wall (never hard-block)

## Epic List

### Epic 1: Core Delivery Pipeline
Sender uploads any-size file (chunked + E2E encrypted), gets a share link, recipient downloads with integrity verification. The fundamental "send → store → share → download" engine, with accounts and quota enforced. File management included so senders can organize their deliveries from day one.

**FRs covered:** FR-1, FR-2, FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-10, FR-11, FR-17

### Epic 2: Professional Delivery Experience
Sender brands the delivery page, protects with password, charges via Ko-fi, sees download tracking. Recipients can verify encryption (trust panel). The professional handoff layer — what makes ZapDrop a studio tool, not just a file pipe.

**FRs covered:** FR-6, FR-12, FR-14, FR-15, FR-16

### Epic 3: Platform Sustainability & Safety
Free tier is ad-funded without poisoning paid users' experience. Abuse is reportable and auto-takedown. Rate guardrails and anti-adblock keep the platform solvent and safe. The operational layer — what keeps the lights on.

**FRs covered:** FR-18, FR-19, FR-20, FR-21, FR-22

---

## Epic 1: Core Delivery Pipeline

### Story 1.1: Storage Backend Foundation

As a developer,
I want the storage provider abstraction and core entity model in place,
So that all upload/download operations have a consistent backend.

**Acceptance Criteria:**

**Given** the app is configured with `StorageProvider: "LocalDisk"`
**When** DI resolves `IStorageProvider`
**Then** `LocalDiskStorageProvider` singleton returned with configured path

**Given** EF Core migration applied
**When** querying schema
**Then** tables: `Deliveries`, `Manifests` (bytea), `Chunks`, `UploadSessions`, `DownloadTracking`; Delivery 1→1 Manifest, 1→N Chunks cascade

**Given** legacy `Files`/`TempShares` exist
**When** migration runs
**Then** new tables for new entities; legacy tables preserved but deprecated

**Given** `StoreChunkAsync` called
**When** valid chunk provided
**Then** chunk persists to disk, returns storage reference

**Given** `DeleteChunksAsync` called
**When** array of chunk IDs provided
**Then** all specified chunks removed from disk

### Story 1.2: Chunked Upload API

As a sender,
I want to upload large files as chunks with resume,
So that files of any size can be sent reliably over unstable connections.

**Acceptance Criteria:**

**Given** authenticated sender
**When** `POST /api/uploads/start` with `{ fileName, fileSize, mimeType }`
**Then** `UploadSession` created with unique `uploadId`, status InProgress, 24h TTL; returns `{ uploadId, chunkSize }`

**Given** active session
**When** `POST /api/uploads/{id}/chunks/{index}` with chunk bytes
**Then** chunk stored via `IStorageProvider`; returns `{ chunkIndex, stored: true }`

**Given** same chunk retried
**When** endpoint called again
**Then** returns `{ alreadyExists: true }` without duplicate storage

**Given** partial upload after disconnect
**When** `GET /api/uploads/{id}/status`
**Then** returns `{ uploadedChunks: [0,1,3], missingChunks: [2,4..9] }`

**Given** all chunks uploaded
**When** `POST /api/uploads/{id}/commit`
**Then** atomic Wolverine transaction: creates Delivery + Manifest, links chunks; returns `{ shareId, shareLink }`

**Given** commit with missing chunks
**When** called
**Then** 400 `{ error: "incomplete_upload", missingChunks: [...] }`

**Given** expired session (24h)
**When** any endpoint called
**Then** 410 `{ error: "session_expired" }`

**Given** Free plan file exceeds limit
**When** start called
**Then** 413 `{ maxSize: "5GB", currentPlan: "Free" }`

### Story 1.3: Recipient Download

As a recipient,
I want to open a link and download without creating an account,
So that receiving files is frictionless.

**Acceptance Criteria:**

**Given** valid `shareId`
**When** `GET /d/{shareId}` accessed
**Then** download page: file name, size, download button; no auth required

**Given** download with fragment key
**When** recipient clicks Download
**Then** chunks stream, SHA-256 verified per-chunk + whole-file, browser receives complete original

**Given** chunk fails SHA-256
**When** hash mismatch
**Then** chunk re-fetched (not whole file); 3 failures → clear error

**Given** expired delivery
**When** accessed
**Then** 410 page: "This delivery has expired" + clock icon + "Request re-send"

**Given** invalid shareId
**When** accessed
**Then** 404: "This link doesn't exist"

**Given** download-cap reached
**When** accessed
**Then** button disabled: "Download limit reached. Ask the sender to re-send."

**Given** successful download
**When** completes
**Then** counter atomically incremented; IP + timestamp logged; EgressBytes updated

### Story 1.4: E2E Encryption

As a sender,
I want files encrypted on my device before upload,
So that no one — not ZapDrop, not the storage — can read them.

**Acceptance Criteria:**

**Given** file selected in browser
**When** upload starts
**Then** WebCrypto generates AES-256-GCM key; file encrypted before chunking; per-chunk unique 96-bit nonce, AAD = `manifest_id ‖ chunk_index`; only ciphertext uploaded

**Given** encryption complete
**When** manifest generated
**Then** manifest encrypted with same key (name, MIME, size, whole-file + per-chunk SHA-256, chunk refs, crypto params); uploaded as ciphertext

**Given** commit succeeds
**When** link generated
**Then** format: `.../d/{shareId}#{base64url-key}`; key in URL fragment only, never in any request

**Given** recipient opens link with key
**When** download starts
**Then** browser extracts key from `window.location.hash`, decrypts manifest, decrypts each chunk with AEAD; failed auth → reject + re-fetch

**Given** link without fragment (key stripped)
**When** browser attempts decrypt
**Then** fails; "Invalid or incomplete link — ask sender for full link"

**Given** DevTools network inspection
**When** reviewing request bodies
**Then** all bodies contain only high-entropy ciphertext; key never appears

### Story 1.5: Upload UI

As a sender,
I want a clean upload interface with real-time progress,
So that I know when my file is ready and can share immediately.

**Acceptance Criteria:**

**Given** sender on `/`
**When** page loads
**Then** centered drop zone: dashed border, upload icon, "Drag files here or click to browse"

**Given** file dragged over zone
**When** drag enters
**Then** border → solid accent, background → `--accent-subtle`, scale 1.01; drag leave reverts

**Given** file selected
**When** accepted
**Then** file preview (icon + name + size) appears; upload begins immediately

**Given** upload in progress
**When** chunks uploading
**Then** animated bar (4px, accent) + "Uploading — 45% · 12.4/27.5 MB · 2.1 MB/s · ~6s"

**Given** encryption stage
**When** encrypting
**Then** text: "Encrypting..."

**Given** upload complete
**When** commit returns link
**Then** bar fills 100%; Share Link Card: file icon+name+size, monospace link field + copy button, "Link copied" toast

**Given** mobile (< 640px)
**When** page loads
**Then** drop zone → full-width "Select files" button (56px, accent)

**Given** upload fails mid-way
**When** chunk error
**Then** red toast: "Upload failed. Check your connection." + Retry; only missing chunks re-upload

**Given** Free plan exceeds limit
**When** file selected
**Then** inline message: "Files over 5 GB require Pro. This file is X GB." + Upgrade/Cancel; no modal

### Story 1.6: Accounts & Plans

As a sender,
I want to sign in with Google and have plan limits enforced,
So that my deliveries are tied to my account and I can upgrade for more capacity.

**Acceptance Criteria:**

**Given** unauthenticated user on `/`
**When** page accessed
**Then** redirected to Google OAuth; on sign-in, User created/matched with Free plan

**Given** authenticated sender
**When** accessing sender pages
**Then** cookie validated server-side; nav shows name + plan

**Given** Free plan sender
**When** uploading
**Then** limits server-enforced: 5 GB/file, 3-day expiry, 5 download-cap, 1-2 concurrency

**Given** Pro plan sender
**When** uploading
**Then** 50 GB/file, 30-day expiry, 50 download-cap, 3-4 concurrency, branded page enabled

**Given** Studio plan sender
**When** uploading
**Then** unlimited (chunked), 30-day expiry, configurable download-cap, 6+ concurrency, branded + paid enabled

**Given** sender exceeds limit
**When** action attempted
**Then** 403/413/429 with clear message + upgrade nudge; client bypass impossible

### Story 1.7: Delivery Controls

As a sender,
I want to control link expiry and download limits,
So that I can protect files and manage costs.

**Acceptance Criteria:**

**Given** upload complete
**When** link card displayed
**Then** shows "Expires in X days"; Free shows plan-limited max

**Given** Pro/Studio before upload
**When** on upload page
**Then** can set custom expiry (up to plan max) and custom download-cap

**Given** active delivery
**When** extend called
**Then** expiry +24h; toast: "Extended by 24h"

**Given** expired delivery
**When** daily cleanup job runs
**Then** chunks deleted, manifest + delivery cascade-deleted, storage reclaimed

**Given** download-cap reached
**When** download attempted
**Then** 410 disabled; "Download limit reached. Ask sender to re-send."

**Given** dashboard card
**When** rendered
**Then** shows "3 of 5 downloads", expiry badge (green/amber/gray), extend icon

### Story 1.8: File Management

As a sender,
I want to view, search, organize, and delete deliveries,
So that I can manage shared files efficiently.

**Acceptance Criteria:**

**Given** sender with deliveries on `/deliveries`
**When** page loads
**Then** grid of Delivery Cards: thumbnail (icon + gradient), name (truncated), status badge, download count, days remaining; hover lifts +2px

**Given** search field used
**When** typing
**Then** 300ms debounced filter by name + folder path; empty → "No deliveries match"

**Given** card clicked
**When** navigating
**Then** `/deliveries/{id}`: full details, metadata, expiry/download status, copy/extend/delete buttons

**Given** delete clicked
**When** confirm dialog accepted
**Then** chunks + manifest + delivery cascade-deleted; redirect with success toast

**Given** copy link icon
**When** clicked on any card
**Then** link copied; "Copied!" toast

**Given** many deliveries
**When** paginating
**Then** paginated; sort: newest, oldest, name A-Z, size; invalid sort → 400

**Given** folders used
**When** organizing
**Then** flat hierarchy (root only); unique paths per user; move between folders; rename updates paths; duplicate → 409

## Epic 2: Professional Delivery Experience

### Story 2.1: Password Protection

As a Pro/Studio sender,
I want to set a password on my delivery,
So that only the intended recipient can access it.

**Acceptance Criteria:**

**Given** Pro/Studio sender on upload page
**When** "Password protect" is toggled
**Then** a password field appears: "Set a password for this delivery"

**Given** password set and delivery created
**When** recipient opens the share link
**Then** password field shown before file card: "This delivery is password-protected."

**Given** correct password entered
**When** submitted
**Then** server verifies, releases Manifest → file card + download render; incorrect → 401 "Wrong password."

**Given** server-side gate (not key derivation)
**When** recipient has fragment key but not password
**Then** key alone useless — server withholds Manifest until password verified

### Story 2.2: Branded Delivery Page

As a Pro/Studio sender,
I want my logo and message on the recipient's download page,
So that my delivery looks professional.

**Acceptance Criteria:**

**Given** Pro/Studio sender on upload page
**When** branding section expanded
**Then** fields: logo upload (max 180px), personal message textarea, accent color picker

**Given** branding configured and delivery created
**When** recipient opens link
**Then** page shows: sender logo (centered), italic message (`--text-lg`, `--fg-muted`), download button in sender's accent color

**Given** Free plan sender
**When** uploading
**Then** no branding options; download page shows generic ZapDrop branding

**Given** Studio sender
**When** branding configured
**Then** background can be solid color or subtle gradient; Free/Pro senders never see ads on their own recipient page

### Story 2.3: Download Tracking

As a sender,
I want to see when and how many times my delivery was downloaded,
So that I know the client received it.

**Acceptance Criteria:**

**Given** sender viewing a delivery card
**When** card renders
**Then** shows download count: "12 downloads" or "3 of 5 downloads" (if cap set)

**Given** sender on `/deliveries/{id}`
**When** page loads
**Then** shows total downloads, last download timestamp ("Downloaded 2h ago" / "Never downloaded"), cap status

**Given** download completes
**When** recipient finishes
**Then** `DownloadTracking` created (IP + timestamp); `Delivery.DownloadCount` atomically incremented; visible to sender immediately

**Given** no downloads yet
**When** sender views delivery
**Then** "Not yet downloaded" with gray clock icon

### Story 2.4: Paid Delivery via Ko-fi

As a Studio sender,
I want to charge for my delivery via Ko-fi,
So that I can sell deliverables directly.

**Acceptance Criteria:**

**Given** Studio sender on upload page
**When** "Require payment" toggled
**Then** price field appears: "Set price (VND)"

**Given** price set and delivery created
**When** recipient opens link
**Then** payment prompt: "This delivery requires payment — X VND" with Ko-fi payment button; file card hidden

**Given** recipient completes Ko-fi payment
**When** webhook `POST /api/payments/ko-fi/webhook` fires with valid signature
**Then** `Delivery.PaymentStatus` → Paid; delivery unlocked atomically; page auto-refreshes → file card + download

**Given** invalid webhook signature
**When** endpoint called
**Then** 403; payment not processed; delivery stays locked

**Given** payment not received (timeout)
**When** recipient returns
**Then** prompt still shows; "Payment not received. The delivery is still available." + retry; no penalty

### Story 2.5: Trust Verification

As a technical user,
I want to verify files are truly encrypted end-to-end,
So that I trust ZapDrop's zero-knowledge claim.

**Acceptance Criteria:**

**Given** any recipient on download page
**When** page renders
**Then** 🔒 "End-to-end encrypted" badge visible below download button (`--text-sm`, `--fg-muted`)

**Given** recipient clicks badge or "Verify encryption"
**When** activated
**Then** expandable panel: "Your files were encrypted with AES-256-GCM before upload. ZapDrop cannot read them — only you hold the decryption key (embedded in your link)." + content hash

**Given** technical user wants deeper check
**When** expanding advanced section
**Then** shows DevTools guide: "Open DevTools (F12) → Network tab. All request bodies are ciphertext. The key (after # in your link) is never sent to any server." + copyable snippet

**Given** non-technical recipient
**When** viewing page
**Then** badge visible but non-intrusive; clicking optional; default experience is clean download

## Epic 3: Platform Sustainability & Safety

### Story 3.1: Rate & Volume Guardrails

As an operator,
I want server-enforced rate limits and volume caps,
So that the platform stays solvent and abuse is contained.

**Acceptance Criteria:**

**Given** any sender uploading
**When** requests exceed per-Plan rate limits
**Then** server returns 429 with retry-after header; consecutive abuse → temporary throttle

**Given** any sender
**When** monthly upload volume exceeds Plan ceiling
**Then** further uploads refused with 403: "Monthly upload limit reached. Upgrade to [next tier] for more."

**Given** IP-based recipient rate limiting
**When** excessive downloads from single IP
**Then** download throttled with 429; abuse logged to DownloadTracking

**Given** rate limits per Plan
**When** limits are in configuration
**Then** Free: most restrictive, Pro: moderate, Studio: generous; all values config-driven, not hardcoded

**Given** metadata-only logging
**When** any request is logged
**Then** logs contain: who (user/IP), when (timestamp), what (shareId/endpoint), size (bytes); never content or plaintext metadata

### Story 3.2: Report → Takedown

As a recipient,
I want to report a suspicious delivery,
So that abusive content is removed quickly.

**Acceptance Criteria:**

**Given** any recipient on a download page
**When** scrolling to bottom
**Then** "Report this delivery" link visible (`--text-xs`, `--fg-muted`)

**Given** report link clicked
**When** activated
**Then** simple form: reason dropdown (Spam, Malware, Illegal content, Other) + optional message textarea + Submit button

**Given** report submitted
**When** form posted
**Then** confirmation: "Thank you. This delivery has been reported and will be reviewed."; delivery immediately locked (server-side): chunks deleted from storage, manifest + delivery cascade-deleted, link returns 410

**Given** report submitted
**When** delivery is takedown
**Then** sender receives notification (email or in-app) that their delivery was reported and removed

**Given** report submitted without auth
**When** any recipient (even unauthenticated) reports
**Then** report is accepted; no account required to report

**Given** duplicate reports on same delivery
**When** already-takedown delivery reported again
**Then** 410 returned; "This delivery is no longer available"

### Story 3.3: Ad Placement on Free Tier

As an operator,
I want ads on Free-tier recipient pages only,
So that the free tier contributes revenue without touching paid users.

**Acceptance Criteria:**

**Given** Free plan sender's delivery
**When** recipient opens the share link
**Then** interstitial ad countdown appears (3-5s): "Your download will start in 3s..." with ad panel alongside; after countdown, ad panel remains below file card; download begins

**Given** same Free-tier link re-opened
**When** recipient returns
**Then** no countdown; ad panel remains below file card

**Given** Pro or Studio sender's delivery
**When** recipient opens the share link
**Then** NO ads, NO countdown — clean page. Ad-free handoff is a sold feature.

**Given** ad rendering decision
**When** page template loads
**Then** `User.PlanId` is looked up via `Delivery.SenderId`; Free → ads injected; Pro/Studio → clean. Invariant at template level, not per-handler.

**Given** ad content standards
**When** ads are served
**Then** network uses curated allowlist + category blocks; gambling permitted but not egregious/malicious content

### Story 3.4: Clean Trial Allowance

As a new sender,
I want my first few deliveries to be ad-free,
So that I can experience the professional handoff before deciding to upgrade.

**Acceptance Criteria:**

**Given** newly created Free account
**When** sender creates their first delivery
**Then** recipient sees a clean (ad-free) download page; `User.CleanDeliveriesRemaining` decremented from 5

**Given** sender has clean deliveries remaining
**When** creating deliveries
**Then** each delivery consumes one clean credit until 0 remain

**Given** sender exhausts clean trial (0 remaining)
**When** creating a new delivery
**Then** recipient sees the ad interstitial + ad panel (Story 3.3 behavior)

**Given** sender upgrades to Pro/Studio
**When** plan changes
**Then** ads never shown regardless of clean trial status; Pro/Studio = always clean

**Given** clean trial count
**When** configurable
**Then** default 5, tunable via configuration without code change

### Story 3.5: Anti-Adblock Soft-Wall

As an operator,
I want to recover ad revenue from adblock users,
So that the free tier remains sustainable — without blocking downloads.

**Acceptance Criteria:**

**Given** Free-tier recipient with adblocker detected
**When** download page loads
**Then** interstitial shows: "Free delivery is supported by ads. Please allow ads to keep ZapDrop free." + "Continue with ads" / "Retry" buttons

**Given** adblock soft-wall shown
**When** user clicks "Continue with ads"
**Then** ad loads (if possible) or page proceeds to download; downloads are NEVER hard-blocked

**Given** legitimate privacy user (Brave, Firefox privacy mode, DNS filtering)
**When** adblock detected
**Then** soft-wall still shows but Retry always available; user can always reach the download

**Given** optional Ad-Shield subdomain serving
**When** configured
**Then** ads served via own subdomain to evade filter lists; but never at the cost of hard-blocking access

**Given** Pro/Studio sender's delivery
**When** recipient opens link
**Then** no adblock detection runs; page is always clean regardless of recipient's adblocker
