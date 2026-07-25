---
stepsCompleted:
  - "step-01: validate-prerequisites (user confirmed ✅)"
  - "step-02: design-epics (2 epics approved ✅)"
  - "step-03: create-stories (19 stories total ✅)"
  - "step-04: final-validation (all checks passed ✅)"
inputDocuments:
  - "_bmad-output/planning-artifacts/prds/prd-TeleCloud-2026-07-19/prd.md"
  - "_bmad-output/architectures/architecture-telecloud-2026-07-20/ARCHITECTURE-SPINE.md"
  - "_bmad-output/specs/spec-r2-storage/SPEC.md"
  - "_bmad-output/specs/spec-r2-storage/upload-flow.md"
  - "_bmad-output/specs/spec-r2-storage/download-flow.md"
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md"
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/EXPERIENCE.md"
---

# ZapDrop - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for ZapDrop (formerly TeleCloud), decomposing the requirements from the PRD, UX design contract, and Architecture spine into implementable stories.

## Requirements Inventory

### Functional Requirements

> **Note:** The PRD has a numbering conflict — two FR-11 (Download-count cap vs Password protection) and two FR-12 (Paid delivery via local rails vs Branded delivery page). They are listed here as written in the PRD and resolved with descriptive suffixes during mapping.

- FR-1: **Split-and-store any-size file** — chunk large files into ordered chunks, store on R2 storage with SHA-256 verification
- FR-2: **Parallel upload with chunk-level resume** — concurrent R2 multipart upload, survive connection loss without restart
- FR-3: **Download reassembly with integrity** — per-chunk + whole-file SHA-256 verification, corrupt chunk re-fetch
- FR-4: **Encrypt-before-upload with key in fragment** — AES-256-GCM, key in URL fragment (#), never transmitted to server
- FR-5: **Independent per-chunk AEAD** — unique nonce per chunk, AAD binds manifest_id + chunk_index, anti-swap/reorder
- FR-6: **Verifiable zero-knowledge** — user can verify ciphertext-only network traffic via browser DevTools inspection
- FR-7: **Recipient-never-authenticates** — download without account, login, or app install
- FR-8: **Sender accounts with plans** — Google OAuth sign-in, Free/Pro/Studio plan tiers
- FR-9: **Server-enforced quota** — per-plan limits (file size, monthly volume, expiry, concurrency) enforced server-side
- FR-10: **Link expiry** — sender sets expiration, expired deliveries return 410, R2 objects reclaimed on expiry
- FR-11: **Download-count cap** — max downloads per link, Plan-bound (Free: 5, Pro: 50, Studio: configurable)
- FR-12: **Paid delivery via VietQR** — sender sets price, recipient pays via VietQR before download unlocks
- FR-13: **Egress passthrough (Studio tier, post-MVP)** — overage billed per-GB beyond monthly allowance
- FR-14: **Password protection** — server-side access gate, withholds Manifest until password verified
- FR-15: **Branded delivery page** — sender logo, message on recipient page (Pro/Studio only)
- FR-16: **Download tracking** — count + last-download timestamp visible to sender on dashboard
- FR-17: **File management** — list/search/sort/paginate deliveries, flat folder hierarchy, delete with cascade
- FR-18: **Rate & volume guardrails** — server-enforced rate limits, per-plan monthly volume ceilings
- FR-19: **Report → takedown** — anyone can report a delivery link, immediate R2 object + manifest deletion
- FR-20: **Plan-scoped ad placement** — interstitial ads only on Free-sender recipient pages; Pro/Studio always clean
- FR-21: **Clean trial allowance** — 3-5 ad-free deliveries for new accounts before ad gate activates
- FR-22: **Anti-adblock recovery** — soft-wall (ask + retry), never hard-block file downloads

### NonFunctional Requirements

- NFR-1: **Vietnamese network throughput** — chunk-size × concurrency defaults benchmarked against VN ISP conditions
- NFR-2: **Tier matrix configurability** — all plan limits (sizes, caps, expiry ceilings) in configuration, not hardcoded
- NFR-3: **Ad-content standards** — curated network allowlist + category blocks, gambling-OK but not egregious
- NFR-4: **Storage cost control** — expiry + download-cap ensure storage/egress cost stays below revenue
- NFR-5: **Desktop-first responsive** — 3 breakpoints (mobile < 640px, tablet 640-1024px, desktop > 1024px)
- NFR-6: **Accessibility** — keyboard navigation, focus indicators, aria-live regions, prefers-reduced-motion respect
- NFR-7: **Dark + light mode** — color tokens support both themes (light + dark), CSS variable swap

### Additional Requirements (from Architecture + SPEC)

- AR-1: **IStorageProvider extended** — IStorageProvider gets R2-specific high-level methods (InitR2MultipartUpload, UploadR2Part, CompleteR2MultipartUpload, AbortR2MultipartUpload, GetPresignedDownloadUrl)
- AR-2: **R2StorageProvider in DI** — R2StorageProvider registered as active; LocalDiskStorageProvider deprecated but kept for backward compat
- AR-3: **Object key scheme** — `uploads/{userId}/{deliveryId}/{fileName}` — deterministic, scoped, no DB lookup needed for R2 access
- AR-4: **Delivery.StoragePath on commit** — populated with R2 object key prefix for storage reclamation
- AR-5: **R2 multipart upload endpoints** — `POST /api/deliveries/start-r2` (returns UploadId), `POST /api/deliveries/commit-r2` (completes multipart, writes Delivery)
- AR-6: **Existing upload handler unchanged** — `StartUploadHandler` stays for backward compat; R2 bypasses chunk-creation flow entirely
- AR-7: **UploadId in UploadSession** — R2 multipart uploadId stored in `UploadSession.R2UploadId`; each part = one encrypted chunk
- AR-8: **Download via 302 redirect** — `DownloadDeliveryHandler` returns 302 to presigned R2 URL (no chunk streaming through server)
- AR-9: **R2 SSE-S3 limitation accepted** — R2 does not support SSE-S3; encryption is automatic AES-256-GCM at rest (no customer-managed key option)
- AR-10: **Error handling fail-fast** — R2 failures return 502 (upstream failure) for upload ops, 400 for validation errors
- AR-11: **Storage reclamation** — On Delivery expiry or deletion, R2 objects deleted using `StoragePath` prefix
- AR-12: **Initiate R2 multipart (CAP-1)** — endpoint receives delivery metadata, returns R2 UploadId
- AR-13: **Upload chunks to R2 part (CAP-2)** — client uploads one chunk as R2 part against UploadId
- AR-14: **Complete R2 multipart (CAP-3)** — server lists uploaded parts, completes multipart, writes Delivery record
- AR-15: **Abort R2 multipart (CAP-4)** — client/expiry aborts incomplete multipart, cleans up uploaded parts
- AR-16: **Serve download via R2 presigned URL (CAP-5)** — handler returns 302 redirect to presigned object URL
- AR-17: **Delete R2 objects on expiry (CAP-6)** — background job deletes R2 objects when Delivery expires or is deleted

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

| FR | Epic | Description |
|----|------|-------------|
| FR-1 | Epic 1 | R2 chunked upload, any-size file |
| FR-2 | Epic 1 | Parallel upload + chunk-level resume |
| FR-3 | Epic 1 | Download reassembly + SHA-256 verification |
| FR-4 | Epic 1 | Encrypt-before-upload, key in URL fragment |
| FR-5 | Epic 1 | Independent per-chunk AEAD with AAD binding |
| FR-6 | Epic 2 | Zero-knowledge verification panel |
| FR-7 | Epic 1 | Recipient never authenticates |
| FR-8 | Epic 1 | Google OAuth + Free/Pro/Studio plans |
| FR-9 | Epic 1 | Server-enforced quota per plan |
| FR-10 | Epic 1 | Link expiry + R2 storage reclamation |
| FR-11 | Epic 1 | Download-count cap per link |
| FR-12 | Epic 2 | Paid delivery via VietQR |
| FR-13 | — | Egress passthrough (post-MVP, deferred) |
| FR-14 | Epic 2 | Password protection (server-side gate) |
| FR-15 | Epic 2 | Branded delivery page (Pro/Studio only) |
| FR-16 | Epic 1 | Download tracking (count + timestamp) |
| FR-17 | Epic 1 | File management CRUD + folders |
| FR-18 | Epic 2 | Rate & volume guardrails |
| FR-19 | Epic 2 | Report → takedown |
| FR-20 | Epic 2 | Plan-scoped ad placement (Free only) |
| FR-21 | Epic 2 | Clean trial allowance |
| FR-22 | Epic 2 | Anti-adblock soft-wall |

## Epic List

### Epic 1: Core Delivery Pipeline

Sender can upload any-size file (chunked, encrypted), get a share link, recipient downloads with integrity verification — all under their account with plan-based quota, expiry, download caps, and file management.

**FRs covered:** FR-1, FR-2, FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-10, FR-11, FR-16, FR-17

**Key deliverables:**
- R2 multipart upload (start-r2 / commit-r2 / abort-r2)
- Download via 302 presigned URL redirect
- Client-side AES-256-GCM encryption (WebCrypto)
- Google OAuth accounts + Free/Pro/Studio plans
- Server-enforced quota per plan
- Link expiry + download-count cap + R2 object reclamation
- Upload UI (drop zone, progress, share link card)
- Download UI (recipient page, integrity verification)
- Dashboard with grid cards, status badges, notifications
- File management CRUD + flat folder hierarchy
- Design token system (colors, typography, spacing, shadows, breakpoints)
- Responsive layout (desktop/tablet/mobile)
- State patterns (empty, loading, error, success)

## Epic 2: Professional Delivery & Platform Safety — Stories

### Story 2.1: Password Protection Gate

As a sender,
I want to password-protect my deliveries,
So that only recipients with the password can download the file.

**Acceptance Criteria:**

**Given** a sender creates a delivery with a password
**When** they set a password in the upload flow
**Then** the password is hashed (bcrypt) and stored server-side alongside the delivery manifest
**And** the share link does NOT contain the password

**Given** a recipient opens a password-protected delivery link
**When** the download page loads
**Then** the `PasswordGateSlot` renders a password input before showing the file card
**And** the Manifest is NOT returned to the client until the password is verified

**Given** the recipient enters the correct password
**When** they submit the form
**Then** the server validates against the stored bcrypt hash
**And** returns the Manifest (file metadata, sizes, integrity hashes)
**And** the download card renders below the password gate

**Given** the recipient enters an incorrect password
**When** they submit the form
**Then** the server returns `401 Unauthorized`
**And** the UI shows a validation error: "Wrong password — try again"
**And** the input retains focus for re-entry

**Files affected:**
- `src/QuickShare.Server/Features/Deliveries/VerifyPassword/VerifyPasswordHandler.cs` — new
- `src/QuickShare.Client/src/features/download/slots/PasswordGateSlot.tsx` — implement
- `src/QuickShare.Server/Models/Delivery.cs` — add `PasswordHash` property
- `src/QuickShare.Client/src/features/download/DownloadPage.tsx` — enable `PasswordGateSlot`

### Story 2.2: Branded Delivery Page (Pro/Studio)

As a Pro or Studio sender,
I want recipients to see my brand on the download page,
So that my deliveries look professional and consistent with my identity.

**Acceptance Criteria:**

**Given** a Pro/Studio sender uploads a delivery
**When** they have branding configured in their account settings
**Then** `BrandingSlot` on the download page renders:
- Sender logo (max 180px width, preserving aspect ratio)
- Sender display name
- Optional personal message
- Accent color applied to buttons and highlights

**Given** a Free sender's delivery page renders
**When** the branding feature flag is false
**Then** `BrandingSlot` renders nothing (no layout shift)
**And** no network request for branding data is made

**Given** accent color is configured
**When** the download page renders
**Then** the CSS variable for accent (`--brand-accent`) is set from the sender's preference
**And** primary buttons, links, and highlights use this color

**Given** the sender updates their branding settings
**When** they change logo, message, or accent color
**Then** all existing delivery pages use the updated branding (read from sender profile at render time)

**Files affected:**
- `src/QuickShare.Client/src/features/download/slots/BrandingSlot.tsx` — implement
- `src/QuickShare.Server/Features/Deliveries/Branding/GetBrandingHandler.cs` — new
- `src/QuickShare.Server/Models/User.cs` — add `Branding` fields

### Story 2.3: VietQR Payment Flow

As a sender,
I want to set a price for my delivery and collect payment via VietQR,
So that recipients pay before they can download.

**Acceptance Criteria:**

**Given** a sender enables paid delivery
**When** they set a price (VND) during upload
**Then** a VietQR payment code is generated for that delivery
**And** the delivery requires payment before the file is accessible

**Given** a recipient opens a paid delivery link
**When** the download page loads
**Then** the `PaymentGateSlot` renders a VietQR code with the amount and bank info
**And** shows "Waiting for payment..." with a timer
**And** the file card is hidden until payment is confirmed

**Given** the recipient scans and pays via their banking app
**When** the VietQR webhook callback arrives
**Then** the server marks the delivery as paid
**And** the download page auto-advances (polling or WebSocket) to show the file card
**And** a success message appears: "Payment received — your file is ready"

**Given** payment is not received within the timeout window
**When** the timer expires
**Then** the page shows "Payment session expired — refresh to retry"
**And** the sender is not charged

**Given** a payment webhook fails or is delayed
**When** the verification is pending
**Then** the page continues polling at a reasonable interval (10s)
**And** the user sees "Waiting for confirmation..."
**And** after 5 minutes, shows "Confirmation taking longer than usual — refresh the page"

**Files affected:**
- `src/QuickShare.Server/Features/Deliveries/Payment/VietQRHandler.cs` — new
- `src/QuickShare.Server/Features/Deliveries/Payment/VietQRWebhookHandler.cs` — new
- `src/QuickShare.Client/src/features/download/slots/PaymentGateSlot.tsx` — implement
- `src/QuickShare.Client/src/features/payment/VietQRDisplay.tsx` — new
- `src/QuickShare.Server/Models/Delivery.cs` — add `Price`, `PaymentStatus` fields

### Story 2.4: Zero-Knowledge Verification Panel

As a recipient,
I want to verify that the file I'm downloading is end-to-end encrypted and the server never saw plaintext,
So that I can trust the privacy claim with technical evidence.

**Acceptance Criteria:**

**Given** a recipient opens any download page
**When** the page renders
**Then** an 🔒 badge with "End-to-End Encrypted" label is visible near the file info

**Given** the recipient clicks or taps the encryption badge
**When** the interaction is triggered
**Then** an expandable panel slides open with:
- Plain-language explanation: "This file was encrypted in your sender's browser before uploading. Even we can't read it."
- Content hash (SHA-256 of the encrypted manifest)
- A DevTools verification snippet the user can copy and run
- "How this works" expandable technical details

**Given** the recipient wants to verify technically
**When** they open their browser DevTools (Network tab)
**Then** they can inspect that all data sent to the server is ciphertext
**And** the panel explains which requests to look for and what to expect

**Given** the panel is open
**When** the recipient clicks the close button or taps outside
**Then** the panel slides closed with a smooth animation

**Files affected:**
- `src/QuickShare.Client/src/features/download/TrustBadge.tsx` — new
- `src/QuickShare.Client/src/features/download/TrustPanel.tsx` — new
- `src/QuickShare.Client/src/features/download/DevToolsSnippet.tsx` — new

### Story 2.5: Rate & Volume Guardrails

As a platform operator,
I want rate limits and monthly volume ceilings enforced server-side,
So that no single user can abuse system resources.

**Acceptance Criteria:**

**Given** an authenticated user exceeds the request rate limit
**When** they send more than N requests per minute (configurable per plan)
**Then** the server returns `429 Too Many Requests` with a `Retry-After` header
**And** the response body includes the rate limit window info

**Given** a user exceeds their plan's monthly upload volume
**When** they attempt to initiate a new upload
**Then** the server returns `403 Forbidden` with "Monthly volume limit reached"
**And** the response body includes the current usage and reset date

**Given** a user is on Free plan (2GB/month)
**When** they've uploaded 2GB worth of deliveries this month
**Then** subsequent upload attempts are blocked until the next billing cycle
**And** existing deliveries and downloads continue to work normally

**Given** a user is on Pro or Studio plan
**When** they approach 80% of their volume limit
**Then** a warning header is added to responses (`X-Volume-Warning: 80%`)
**And** the dashboard shows a usage meter

**Files affected:**
- `src/QuickShare.Server/Middleware/RateLimitingMiddleware.cs` — new
- `src/QuickShare.Server/Infrastructure/Quota/VolumeTracker.cs` — new (or extend QuotaEnforcer)
- `src/QuickShare.Client/src/features/dashboard/UsageMeter.tsx` — new

### Story 2.6: Report → Takedown

As a platform,
I want anyone to report a delivery link for abuse,
So that inappropriate content can be removed immediately.

**Acceptance Criteria:**

**Given** a recipient (or anyone) visits any download page
**When** they see content they believe is abusive
**Then** a "Report this delivery" link is visible at the bottom of the page

**Given** the visitor clicks the report link
**When** the report form loads
**Then** it shows:
- Reason dropdown (Spam, Copyright, Malware, Harassment, Other)
- Optional free-text message field
- Submit button

**Given** the report is submitted
**When** the server receives the report
**Then** it immediately:
- Sets the delivery `Status` to `Takedown`
- Deletes all R2 objects for that delivery via `DeleteObjectsAsync(StoragePath)`
- Creates a `Report` record with reporter info, reason, timestamp
- Returns a confirmation: "Report submitted — content has been removed"

**Given** the original sender visits their dashboard
**When** they see the delivery status
**Then** it shows as "Takedown" with a notice that the content was reported
**And** they cannot restore the delivery

**Given** the same delivery link is visited after takedown
**When** the handler processes the request
**Then** it returns `410 Gone` with "This content has been removed due to a report"

**Files affected:**
- `src/QuickShare.Server/Features/Reports/CreateReport/CreateReportHandler.cs` — new
- `src/QuickShare.Client/src/features/report/ReportForm.tsx` — new
- `src/QuickShare.Client/src/features/download/DownloadPage.tsx` — add report link
- `src/QuickShare.Server/Models/Report.cs` — new entity

### Story 2.7: Ad Interstitial + Clean Trial + Anti-Adblock Soft-Wall

As a platform operator,
I want Free-tier recipients to see ads while ensuring a clean trial for new users,
So that the platform generates revenue without blocking files.

**Acceptance Criteria:**

**Given** a Free-tier sender's delivery is viewed by a recipient
**When** the download page loads
**Then** a 3-5 second countdown overlay displays an ad
**After** the countdown, an ad panel remains visible below the file card
**And** revisits to the same link skip the countdown (cookie/session flag)
**And** the download button is available immediately (ad does not block download)

**Given** a Pro or Studio sender's delivery page loads
**When** the page renders
**Then** no ad is shown (feature flag `enableAds = false`)
**And** the `AdInterstitialSlot` renders nothing

**Given** a new account is created
**When** the user sends their first 3-5 deliveries
**Then** recipients see NO ads on those deliveries (clean trial)
**After** the trial allowance is consumed
**Then** subsequent deliveries from this sender show ads to Free-tier recipients

**Given** an adblocker is detected (via script probe)
**When** the ad is blocked
**Then** a soft-wall message appears: "Please disable your ad blocker to support the service"
**And** a "Continue to download" button is always visible (never hard-block)
**And** the file download is never prevented by adblock detection

**Files affected:**
- `src/QuickShare.Client/src/features/download/slots/AdInterstitialSlot.tsx` — implement
- `src/QuickShare.Client/src/features/ads/AdOverlay.tsx` — new (countdown + ad)
- `src/QuickShare.Client/src/features/ads/AdPanel.tsx` — new (post-download ad panel)
- `src/QuickShare.Client/src/features/ads/AdblockDetector.ts` — new (probe script)
- `src/QuickShare.Client/src/features/ads/TrialTracker.ts` — new
- `src/QuickShare.Server/Features/Accounts/TrialService.cs` — new

### Story 2.8: Branding Settings UI (Pro/Studio)

As a Pro or Studio sender,
I want to configure my brand settings from my account page,
So that my deliveries display my logo, message, and colors automatically.

**Acceptance Criteria:**

**Given** a Pro or Studio user navigates to account settings
**When** they open the Branding section
**Then** they see:
- Logo upload area (drag-drop or click, max 180px width preview, file size limit)
- Display name field (pre-filled from account name)
- Personal message text area (max 200 chars)
- Accent color picker (preset palette + custom hex input)
- Save button

**Given** the user uploads a logo image
**When** the file is selected
**Then** it is validated (max 500KB, PNG or JPG)
**And** a preview is shown immediately
**And** the image is uploaded to R2 with key `branding/{userId}/logo`

**Given** the user changes accent color
**When** they select from the palette or type a hex value
**Then** a live preview updates to show the new color on buttons and highlights
**And** the color is validated as accessible (WCAG contrast ratio)

**Given** the user clicks Save
**When** the form is submitted
**Then** all branding fields are persisted to the user profile
**And** a success toast appears: "Branding saved"
**And** all existing delivery pages reflect the update on next load

**Files affected:**
- `src/QuickShare.Client/src/features/settings/BrandingSettings.tsx` — new
- `src/QuickShare.Client/src/features/settings/LogoUploader.tsx` — new
- `src/QuickShare.Client/src/features/settings/ColorPicker.tsx` — new
- `src/QuickShare.Server/Features/Accounts/UpdateBrandingHandler.cs` — new

**FRs covered:** FR-6, FR-12, FR-14, FR-15, FR-18, FR-19, FR-20, FR-21, FR-22

**Key deliverables:**
- Password protection (server-side Manifest gate)
- Branded delivery page (logo, message, accent color)
- VietQR payment flow + webhook verification
- Zero-knowledge trust verification panel
- Rate & volume guardrails (429/403)
- Report → takedown endpoint (immediate R2 deletion)
- Ad interstitial on Free-tier recipient pages
- Clean trial allowance (3-5 ad-free deliveries)
- Anti-adblock soft-wall (never hard-block)

## Epic 1: Core Delivery Pipeline — Stories

### Story 1.1: R2 Infrastructure & IStorageProvider Extension

As a developer,
I want R2 storage integrated as the active backend with a clear provider interface,
So that upload and download flows can use Cloudflare R2 without touching the legacy local-disk backend.

**Acceptance Criteria:**

**Given** the application starts
**When** DI container resolves `IStorageProvider`
**Then** the active implementation is `R2StorageProvider`
**And** `LocalDiskStorageProvider` is still registered but marked deprecated

**Given** the project has `IStorageProvider` interface defined
**When** I extend it with R2-specific methods:
- `InitR2MultipartUploadAsync(deliveryId, fileName) → R2InitResult`
- `GenerateUploadPartUrlsAsync(uploadId, partNumbers) → R2PartUrl[]`
- `CompleteR2MultipartUploadAsync(uploadId, deliveryId) → CompletionResult`
- `AbortR2MultipartUploadAsync(uploadId) → void`
- `GetPresignedDownloadUrlAsync(objectKey, expiry) → string`
- `DeleteObjectsAsync(prefix) → void`
**Then** `R2StorageProvider` implements all methods with real R2 API calls
**And** `LocalDiskStorageProvider` throws `NotSupportedException` for new R2 methods

**Given** the R2 config is loaded
**When** `R2StorageProvider` initializes
**Then** it reads `AccountId`, `BucketName`, `AccessKeyId`, `AccessKeySecret` from configuration
**And** validates credentials with a `HeadBucket` call on startup (fail-fast)

**Given** a delivery is created
**When** `Delivery.StoragePath` is set on commit
**Then** the key follows the scheme `uploads/{userId}/{deliveryId}/{fileName}`
**And** no additional DB lookup is needed to construct the R2 object key prefix

**Given** a delivery expires or is deleted
**When** the background cleanup job runs
**Then** it calls `DeleteObjectsAsync(StoragePath)` to delete all objects with that prefix
**And** confirms deletion via R2 response (not blind delete)

**Given** R2 returns a transient error (5xx, timeout)
**When** a storage operation fails
**Then** the handler returns `502 Bad Gateway` with a structured error body
**And** validation errors (400) are not wrapped as 502

**Files affected:**
- `src/QuickShare.Server/Abstractions/IStorageProvider.cs` — extend interface
- `src/QuickShare.Server/Infrastructure/Storage/R2StorageProvider.cs` — new implementation
- `src/QuickShare.Server/Infrastructure/Storage/LocalDiskStorageProvider.cs` — stub new methods
- `src/QuickShare.Server/Models/Delivery.cs` — add `StoragePath` property
- `src/QuickShare.Server/Models/UploadSession.cs` — add `R2UploadId` property
- `appsettings.json` — R2 config section

### Story 1.2: Auth & Plan Tiers (Google OAuth + Free/Pro/Studio)

As a sender,
I want to sign in with Google and get a plan with appropriate limits,
So that I can upload files within my quota and upgrade when I need more capacity.

**Acceptance Criteria:**

**Given** a visitor clicks "Sign in with Google"
**When** they complete OAuth flow
**Then** a new account is created (or existing account is matched)
**And** they receive a Free plan by default
**And** they are redirected to the dashboard

**Given** an authenticated user exists
**When** they access any upload/download management endpoint
**Then** the server enforces plan-based limits from configuration:
- Free: max 100MB per file, 5 downloads per link, 7-day expiry, 2GB monthly volume
- Pro: max 2GB per file, 50 downloads per link, 30-day expiry, 50GB monthly volume
- Studio: max 20GB per file, configurable downloads, 90-day expiry, 500GB monthly volume

**Given** a user exceeds their plan quota
**When** they attempt an upload
**Then** the server returns `403 Forbidden` with a structured error body indicating which limit was hit
**And** the quota check runs server-side (not client-side enforcement)

**Given** an unauthenticated user visits the site
**When** they try to access protected endpoints (upload, dashboard)
**Then** they receive `401 Unauthorized`
**And** the client redirects to the sign-in page

**Files affected:**
- `src/QuickShare.Server/Auth/GoogleOAuthHandler.cs` — new
- `src/QuickShare.Server/Models/User.cs` — plan, quotas
- `src/QuickShare.Server/Infrastructure/Quota/QuotaEnforcer.cs` — new
- `appsettings.json` — plan tier config

### Story 1.3: R2 Multipart Upload — Presign All, Encrypt → R2, Zombie Sweep

As a sender,
I want to upload large files in encrypted chunks directly to R2 with part-level resume,
So that even multi-GB files upload reliably and never touch the server.

**Acceptance Criteria:**

**Given** a sender is authenticated and within quota
**When** they initiate an upload via `POST /api/deliveries/start-r2`
**Then** the server calls `InitR2MultipartUploadAsync` to create an R2 multipart upload
**And** generates presigned PUT URLs for all parts in one batch via `GenerateUploadPartUrlsAsync`
**And** returns `{ deliveryId, uploadId, parts: [{ partNumber, url }] }`
**And** a new `UploadSession` is persisted with `R2UploadId` and `DeliveryId`

**Given** the client has the presigned URLs
**When** it encrypts a chunk (WebCrypto AES-256-GCM)
**Then** it PUTs the encrypted chunk directly to R2 using the presigned URL
**And** records the `ETag` returned by R2 for that part

**Given** all parts are uploaded
**When** the client calls `POST /api/deliveries/commit-r2` with `{ uploadId, parts: [{ partNumber, etag }] }`
**Then** the server calls `CompleteR2MultipartUploadAsync` (ListParts + Complete)
**And** writes a `Delivery` record into the database
**And** returns the delivery share link

**Given** the client cancels mid-upload
**When** it calls `POST /api/deliveries/abort-r2` with `{ uploadId }`
**Then** the server calls `AbortR2MultipartUploadAsync`
**And** deletes the `UploadSession`
**And** all uploaded R2 parts are cleaned up by R2

**Given** a sender starts an upload but never commits
**When** the `UploadSession` TTL expires (24h from creation)
**Then** a background job queries for expired sessions with non-null `R2UploadId`
**And** calls `AbortR2MultipartUploadAsync` for each
**And** deletes the `UploadSession` record

**Given** any R2 API call fails during `start-r2` or `commit-r2`
**When** the error is transient (5xx, timeout)
**Then** the server returns `502 Bad Gateway`
**And** the upload remains in a recoverable state

**Files affected:**
- `src/QuickShare.Server/Features/Uploads/StartR2/StartR2Handler.cs` — new
- `src/QuickShare.Server/Features/Uploads/CommitR2/CommitR2Handler.cs` — new
- `src/QuickShare.Server/Features/Uploads/AbortR2/AbortR2Handler.cs` — new
- `src/QuickShare.Server/BackgroundJobs/ZombiePartSweeper.cs` — new
- `src/QuickShare.Server/Infrastructure/Storage/R2StorageProvider.cs` — implement multipart methods
- `src/QuickShare.Server/Infrastructure/Storage/IStorageProvider.cs` — add new R2 methods

### Story 1.4: Download via 302 Redirect & Byte-Range Resume

As a sender,
I want recipients to download files via direct R2 presigned URLs with byte-range support,
So that downloads are fast, server-free, and interrupted downloads can resume.

**Acceptance Criteria:**

**Given** a recipient visits a delivery link that is valid, not expired, and has downloads remaining
**When** the request does NOT include a `Range` header
**Then** `DownloadDeliveryHandler` generates a **7-day presigned GET URL**
**And** increments the delivery download count
**And** returns `302 Found` with `Location` set to the presigned URL

**Given** a request includes a `Range` header
**When** the handler generates the presigned URL
**Then** it creates a **1-hour scoped presigned URL**
**And** does NOT increment the download count
**And** returns `302 Found` with `Location` set to the presigned URL

**Given** presigned URL TTL is generated
**When** the remaining delivery expiry is shorter than the default TTL
**Then** the URL expiry is bounded by `min(defaultTtl, deliveryExpiry)`

**Given** the delivery is expired
**When** `DownloadDeliveryHandler` receives the request
**Then** it returns `410 Gone` with an expiry message

**Given** the download count cap is reached
**When** the handler checks count before generating the URL
**Then** it returns `403 Forbidden` with "Download limit reached"

**Given** the delivery does not exist or the code is invalid
**When** the handler processes the request
**Then** it returns `404 Not Found`

**Files affected:**
- `src/QuickShare.Server/Features/Downloads/Download/DownloadDeliveryHandler.cs` — refactor for 302 + Range
- `src/QuickShare.Server/Infrastructure/Storage/IStorageProvider.cs` — `GetPresignedDownloadUrlAsync`
- `src/QuickShare.Server/Features/Downloads/Models/DownloadResult.cs` — new

### Story 1.5: Client-Side Encryption (WebCrypto AES-256-GCM)

As a sender,
I want my files encrypted before they leave my browser,
So that the server never sees plaintext content and I maintain zero-knowledge.

**Acceptance Criteria:**

**Given** a file is selected for upload
**When** the client reads the file into an `ArrayBuffer`
**Then** it generates a random 256-bit AES key and a random 96-bit IV
**And** the key is placed in the URL fragment (`#key=base64`) — never sent to the server

**Given** the file is split into fixed-size chunks
**When** each chunk is encrypted
**Then** AES-256-GCM is used with a unique nonce per chunk
**And** the AAD (Additional Authenticated Data) binds `manifest_id + chunk_index`
**And** the ciphertext is uploaded as one R2 part

**Given** the sender copies the share link
**When** the link contains `#key=base64`
**Then** the fragment is never transmitted in server requests (browser spec)
**And** the recipient page reads the key from `window.location.hash` to decrypt

**Given** the recipient pastes the link
**When** the download completes
**Then** the client reassembles chunks in order
**And** decrypts each chunk with AES-256-GCM using the key from the fragment
**And** verifies per-chunk integrity via AEAD authentication tag
**And** reconstructs the original file for the user

**Files affected:**
- `src/QuickShare.Client/src/lib/crypto.ts` — new (WebCrypto wrapper)
- `src/QuickShare.Client/src/lib/chunker.ts` — new (split/reassemble)
- `src/QuickShare.Client/src/workers/encryption.worker.ts` — new (offload from UI thread)
- `src/QuickShare.Client/src/hooks/useEncryption.ts` — new

### Story 1.6: Upload UI — Drop Zone, Progress, Share Link Card

As a sender,
I want a clear drag-and-drop upload interface with visible progress,
So that I know my file is being encrypted and uploaded successfully.

**Acceptance Criteria:**

**Given** a sender is on the upload page
**When** they drag a file over the drop zone
**Then** the zone shows a hover state (dashed border → solid accent border)
**And** an overlay message appears ("Drop to upload")
**When** they drop the file (or click to browse + select)
**Then** the upload begins immediately

**Given** the upload is in progress
**When** encryption and upload are running
**Then** the UI shows an animated progress bar with percentage, speed (MB/s), and ETA
**And** a label alternates between "Encrypting..." and "Uploading..." as phases change

**Given** the upload completes successfully
**When** the `commit-r2` response returns the share link
**Then** the progress transitions to a **Share Link Card** with:
- File icon + file name + file size
- Read-only monospace link field with a `Copy` button
- Expiry label (e.g., "Expires in 7 days")
- Visual confirmation toast on copy

**Given** the upload fails mid-way
**When** a part upload fails (network loss, transient error)
**Then** the UI pauses with a "Retry" button
**And** resumes from the last successfully uploaded part (part-level resume)
**And** shows an error toast with the failure reason

**Given** the network is lost for more than 30 seconds
**When** auto-retry attempts fail
**Then** the UI pauses with "Connection lost — click Resume to continue"
**And** preserves the `UploadSession` for later resume

**Files affected:**
- `src/QuickShare.Client/src/features/upload/UploadDropzone.tsx` — new
- `src/QuickShare.Client/src/features/upload/UploadProgress.tsx` — new
- `src/QuickShare.Client/src/features/upload/ShareLinkCard.tsx` — new
- `src/QuickShare.Client/src/features/upload/useUpload.ts` — new (orchestration hook)
- `src/QuickShare.Client/src/features/notifications/Toast.tsx` — new (shared toast)

### Story 1.7: Download UI — Recipient Page, Integrity Verification

As a recipient,
I want to download shared files with a single click and verify their integrity,
So that I can trust the file I receive is exactly what was sent.

**Acceptance Criteria:**

**Given** a recipient opens a delivery link (without `#key` — link is incomplete)
**When** the page loads
**Then** it shows a placeholder: "This link needs a decryption key — paste the full link"
**And** a single-line text input for the key

**Given** the full link opens (with `#key`)
**When** the page loads
**Then** it displays:
- File name and size
- A prominent "Download" button
- An encryption badge ("🔒 End-to-End Encrypted")
- Download count display ("X of Y downloads remaining")
- A "Report this delivery" link

**Given** the recipient clicks "Download"
**When** the delivery is valid and within cap
**Then** the browser fetches the encrypted file from the presigned R2 URL
**And** decrypts chunks in sequence using the key from `window.location.hash`
**And** reassembles into the original file
**And** triggers a browser download when ready

**Given** integrity check runs
**When** a chunk fails AEAD authentication (tag mismatch)
**Then** the client re-fetches that specific chunk (byte-range on the presigned URL)
**And** retries decryption
**After** 3 consecutive failures on the same chunk
**Then** shows "Download corrupted — please try again" with a retry button

**Given** the download cap is reached
**When** the page loads
**Then** the download button is disabled
**And** shows "Download limit reached" with a message to the sender

**Given** the link is expired
**When** the page loads (410 from server)
**Then** an expired-state page is displayed with "This link has expired — ask the sender to re-share"

**Files affected:**
- `src/QuickShare.Client/src/features/download/DownloadPage.tsx` — new
- `src/QuickShare.Client/src/features/download/DecryptWorker.ts` — new
- `src/QuickShare.Client/src/features/download/useDownload.ts` — new

### Story 1.8: Link Lifecycle — Expiry, Download-Cap Enforcement, R2 Reclamation

As a system,
I want expired deliveries to automatically clean up and cap enforcement to block over-downloads,
So that storage costs stay controlled and sender limits are honored.

**Acceptance Criteria:**

**Given** a delivery has a configured expiry (`Delivery.ExpiresAt`)
**When** a background job runs (every 5 minutes)
**Then** it queries for deliveries where `ExpiresAt < UtcNow` and `Status != Expired`
**And** for each expired delivery it:
- Changes `Status` to `Expired`
- Calls `DeleteObjectsAsync(Delivery.StoragePath)` to remove R2 objects
- Logs the cleanup result

**Given** a delivery has a download cap (`Delivery.MaxDownloads`)
**When** a no-`Range` download request arrives
**Then** the handler increments `Delivery.DownloadCount`
**And** if `DownloadCount >= MaxDownloads` after increment, marks the delivery as cap-reached
**And** subsequent requests return `403 Forbidden` with "Download limit reached"

**Given** a delivery is deleted by the sender
**When** the delete endpoint is called
**Then** the delivery is soft-deleted (or status set to `Deleted`)
**And** R2 objects are deleted immediately via `DeleteObjectsAsync`
**And** the share link returns `410 Gone`

**Given** the cleanup job encounters an R2 error during deletion
**When** the S3 API returns an error
**Then** the job retries up to 3 times with exponential backoff
**And** logs a warning if deletion fails permanently

**Files affected:**
- `src/QuickShare.Server/BackgroundJobs/DeliveryExpiryJob.cs` — new
- `src/QuickShare.Server/Features/Deliveries/DeliveryService.cs` — add cap increment + deletion
- `src/QuickShare.Server/Infrastructure/Storage/IStorageProvider.cs` — `DeleteObjectsAsync`

### Story 1.9: Dashboard & File Management

As a sender,
I want to see all my deliveries in a grid with status badges and management actions,
So that I can track, copy, extend, or delete my shared files.

**Acceptance Criteria:**

**Given** a sender is authenticated
**When** they navigate to the dashboard
**Then** the page shows a responsive grid of delivery cards (desktop: 3-col, tablet: 2-col, mobile: 1-col)

**Given** the dashboard loads with deliveries
**When** each card renders
**Then** it shows:
- Thumbnail area (file type icon)
- File name
- Status badge (green = active, amber = expires <24h, gray = expired)
- Download count ("X / Y downloads")
- Days remaining (or "Expired")
- Action icons: Copy link, Extend expiry, Delete

**Given** the sender has no deliveries yet
**When** the dashboard loads
**Then** an empty state is shown with an illustration and "Your first delivery is one click away" + Upload CTA

**Given** the dashboard is loading
**When** data is being fetched
**Then** skeleton cards with shimmer animation are displayed (3 per row)

**Given** the sender types in the search bar
**When** they enter text
**Then** deliveries are filtered by file name in real-time (debounced 300ms)

**Given** the sender clicks Copy on a card
**When** the action is triggered
**Then** the delivery share link is copied to clipboard
**And** a success toast appears ("Link copied!")

**Given** the sender clicks Delete on a card
**When** they confirm in the confirmation dialog
**Then** the delivery is deleted (server cleanup + R2 reclamation)
**And** the card is removed from the grid with a fade-out animation
**And** a success toast appears ("Delivery deleted")

**Given** a sender clicks Extend on an active delivery
**When** the extension is within plan limits
**Then** the expiry is extended by the plan's max duration
**And** the card updates with the new expiry date

**Files affected:**
- `src/QuickShare.Client/src/features/dashboard/DashboardPage.tsx` — new
- `src/QuickShare.Client/src/features/dashboard/DeliveryCard.tsx` — new
- `src/QuickShare.Client/src/features/dashboard/DeliveryGrid.tsx` — new
- `src/QuickShare.Client/src/features/dashboard/StatusBadge.tsx` — new
- `src/QuickShare.Client/src/features/dashboard/EmptyState.tsx` — new
- `src/QuickShare.Client/src/features/dashboard/SkeletonCard.tsx` — new
- `src/QuickShare.Client/src/features/dashboard/useDeliveries.ts` — new
- `src/QuickShare.Server/Features/Deliveries/List/ListDeliveriesHandler.cs` — new
- `src/QuickShare.Server/Features/Deliveries/Delete/DeleteDeliveryHandler.cs` — new
- `src/QuickShare.Server/Features/Deliveries/Extend/ExtendDeliveryHandler.cs` — new

### Story 1.10: Design Token System & Responsive Layout

As a developer,
I want a consistent design token system with semantic colors, typography, spacing, and responsive breakpoints,
So that all UI components share a coherent visual language and work across desktop, tablet, and mobile.

**Acceptance Criteria:**

**Given** the design system is defined
**When** CSS custom properties are loaded
**Then** the following token categories are available:

**Colors:**
- Brand: navy `#1A1A2E` + accent `#4F6CF7`
- Semantic tokens: `--bg-primary`, `--bg-secondary`, `--bg-card`, `--text-primary`, `--text-secondary`, `--border-default`, `--border-focus`, `--border-error`
- Status tokens: `--status-success`, `--status-warning`, `--status-error`

**Light & Dark themes:**
- `:root` defines light theme values
- `.dark` class swaps semantic tokens for dark mode
- All components use `var(--token)` — no hardcoded colors

**Typography:**
- Primary font: Inter (sans-serif)
- Monospace font: JetBrains Mono (for code, share links)
- Type scale: xs (12px), sm (14px), base (16px), lg (18px), xl (24px), 2xl (32px)

**Spacing:**
- Scale: 4px → 8px → 12px → 16px → 24px → 32px → 48px → 64px

**Border radius:**
- Scale: sm (4px), md (8px), lg (12px), xl (16px), full (9999px)

**Shadows:**
- sm, md, lg, xl — progressively deeper elevation

**Responsive breakpoints:**
- Mobile: `< 640px` (1-col layout, bottom tab bar 56px)
- Tablet: `640px – 1024px` (2-col layout)
- Desktop: `> 1024px` (3-col layout, max content width 1200px)

**Accessibility:**
- All interactive elements are keyboard navigable (Tab, Enter, Escape)
- Focus rings use `--border-focus` (2px solid, offset 2px)
- Icon buttons have `aria-label`
- Toast notifications use `aria-live="polite"`
- `prefers-reduced-motion` disables all animations

**Microcopy:**
- All UI text is in Vietnamese + English
- Direct, honest tone — no AI hype words
- Actionable, brief

**Files affected:**
- `src/QuickShare.Client/src/styles/tokens.css` — new
- `src/QuickShare.Client/src/styles/global.css` — new
- `src/QuickShare.Client/src/styles/responsive.css` — new
- `src/QuickShare.Client/src/styles/dark.css` — new
- `src/QuickShare.Client/src/index.html` — Inter + JetBrains Mono CDN links

### Story 1.11: Download Page Template with Epic 2 Hooks

As a developer,
I want the download page built with conditional slots for Epic 2 features from day one,
So that Epic 2 stories (branding, password, payment, ads) slot in without touching the template.

**Acceptance Criteria:**

**Given** the download page template is built
**When** it renders for any recipient
**Then** it includes the following conditional slots, each gated by a feature flag:

```
<BrandingSlot />         ← if enabled: sender logo + message + accent color
<PasswordGateSlot />     ← if enabled: password form before file card
<PaymentGateSlot />      ← if enabled: VietQR payment UI before file card
<AdInterstitialSlot />   ← if enabled: ad overlay + ad panel
```

**Given** no Epic 2 features are active
**When** all flags are `false`
**Then** the template renders identically to a plain download page
**And** empty slots render nothing (no layout shift)
**And** no extra network requests are made for disabled features

**Given** a feature flag is toggled on
**When** the corresponding slot's feature becomes active
**Then** the slot renders without requiring any template changes
**And** the layout adapts naturally (cards shift down, password appears above file card, etc.)

**Given** the download page is in its default state (Epic 1 only)
**When** it renders
**Then** it shows: download button, file info, encryption badge, report link, download count
**And** the slot containers are present in the DOM but empty (display:none or null-rendered based on flag)

**Files affected:**
- `src/QuickShare.Client/src/features/download/DownloadPage.tsx` — refactor with conditional slot composition
- `src/QuickShare.Client/src/features/download/slots/BrandingSlot.tsx` — placeholder
- `src/QuickShare.Client/src/features/download/slots/PasswordGateSlot.tsx` — placeholder
- `src/QuickShare.Client/src/features/download/slots/PaymentGateSlot.tsx` — placeholder
- `src/QuickShare.Client/src/features/download/slots/AdInterstitialSlot.tsx` — placeholder
- `src/QuickShare.Client/src/features/download/hooks/useFeatureFlags.ts` — new (feature flag provider)


