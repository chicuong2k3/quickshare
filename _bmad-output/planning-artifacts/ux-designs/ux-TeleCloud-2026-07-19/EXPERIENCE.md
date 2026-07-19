---
name: ZapDrop
type: experience
purpose: behavioral-contract
status: final
created: 2026-07-19
updated: 2026-07-19
finalized: 2026-07-19
sources:
  - "_bmad-output/planning-artifacts/prds/prd-TeleCloud-2026-07-19/prd.md"
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md"
companions:
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/DESIGN.md"
---

# EXPERIENCE — ZapDrop

*Behavioral contract. How it works. Formerly TeleCloud — renamed 2026-07-19 per PRD pivot.*

---

## Foundation

### Form Factor
- **Primary:** Desktop-first (creative professionals at workstations)
- **Secondary:** Mobile-responsive parity (management on-the-go)
- **Strategy:** responsive SPA via Blazor SSR + RizzyUI. Same codebase, adaptive layout.
- **Breakpoints:** Mobile < 640px | Tablet 640–1024px | Desktop > 1024px

### Tech Stack Context
- Blazor Static SSR (no WASM, no interactivity server)
- RizzyUI component library (card, alert, heading, badge, etc.)
- Minimal JavaScript for client-side interactions (copy-to-clipboard, file upload preview, toast notifications)
- Tailwind CSS for styling (via RizzyUI conventions)
- Antiforgery tokens for all POST/endpoints

### Visual Identity Reference
- See [DESIGN.md](./DESIGN.md) for complete visual specifications (colors, typography, spacing, components)

---

## Information Architecture

### Site Structure

```
/ (home/upload)        → Upload page — core action, first screen
/deliveries            → Dashboard — grid of all past deliveries
/deliveries/{id}       → Delivery detail — stats, manage expiry, view recipients
/s/{shareCode}         → Public download page — branded (for recipients)
/account               → Account settings — profile, plan, avatar
/account/billing       → Billing & plan management (future)
/pricing               → Pricing page — Free vs Pro vs Studio
/logout                → Sign out
```

### Navigation

**Desktop:**
- Minimal top bar: logo (left) → Upload | Deliveries | Account (right)
- No sidebar. No mega-menu.
- Active page indicated by subtle accent underline on nav item.

**Mobile:**
- Bottom tab bar (56px): Upload | Deliveries | Account
- No hamburger menu — 3 tabs cover all primary destinations
- Logo visible in top bar on mobile too (compact, 32px height)

### Screen Hierarchy

1. **Upload page** (/) — The landing page. Always the first screen after login.
2. **Dashboard** (/deliveries) — Secondary screen for managing past shares.
3. **Delivery Detail** (/deliveries/{id}) — Tertiary for deeper management.
4. **Download Page** (/s/{shareCode}) — Standalone, no nav chrome. Full-screen branded experience.
5. **Account** (/account) — Settings, accessible from nav.

---

## Voice and Tone

### Principles
- **Human, not corporate** — Write like a helpful colleague, not a legal document.
- **Direct, not pushy** — Tell them what happened, what to do next. No "you'll love this!"
- **Honest, not hype** — Free tier limitations are stated plainly. No hidden tricks.
- **Brief, not verbose** — Every word earns its place. Delete anything skimmable.

### Microcopy Patterns

| Context | Tone | Example |
|---------|------|---------|
| Upload in progress | Informative, calm | "Uploading — 2 files · 14.2 MB / 27.5 MB" |
| Upload complete | Simple confirmation | "Link copied to clipboard" |
| Expired link | Apologetic, helpful | "This delivery has expired. Ask the sender to re-send." |
| Upgrade prompt | Contextual, not naggy | "Larger files? Pro plan supports up to 10 GB." (shown below a failed upload) |
| Empty state | Encouraging, helpful | "No deliveries yet — your first share is one click away" |
| Error | Direct, actionable | "Connection lost. Retrying in 10s..." |
| Password prompt | Simple, clear | "This delivery is password-protected. Enter the password to download." |
| Encryption badge | Reassuring, minimal | "🔒 End-to-end encrypted" (hover tooltip: "Encrypted on the sender's device. ZapDrop cannot read this file.") |
| Paid delivery prompt | Direct, transparent | "This delivery requires payment — 150,000 VND" with VietQR code below |
| Report confirmation | Grateful, simple | "Thank you. This delivery has been reported." |
| Ad countdown | Honest, not sneaky | "Your download will start in 3 seconds... Free delivery — supported by ads." |

### Language Rules
- Use Vietnamese and English naturally based on locale. Default: English with Vietnamese fallback.
- Avoid: "Seamless", "Revolutionary", "Next-gen", "Game-changing", "Robust", "Leverage"
- Use: "Send", "Share", "Download", "Upload", "Copy", "Link", "File", "Delivery"
- For technical features: plain language first, technical detail optional (progressive disclosure)

---

## Component Patterns

### Upload Drop Zone
**Behavior:**
- Default state: dashed border, "Drag files here or click to browse" text
- Hover/drag-over: border solidifies, background tints, slight scale
- On file drop/select: immediately begins upload (no "compose" step)
- Single upload at a time (Huy sends one 5GB project, not multiple)
- Shows file preview (icon by type) + name + size immediately after selection
- Click anywhere on drop zone re-opens file picker (even after file selected)

**Limits:**
- Free: ~5 GB max per file, shown as inline hint below drop zone
- Pro: ~50 GB
- Studio: unlimited (chunked transparently)

### Upload Progress
**Behavior:**
- Appears below drop zone immediately on upload start
- Shows: progress bar + percentage + speed + ETA in a single line
- If encryption enabled: "Encrypting..." label replaces speed during final 10%
- On completion: bar fills to 100%, text transitions to "Link copied to clipboard"
- Show the share link card automatically after completion (no additional click)

### Share Link Card
**Behavior:**
- Appears automatically after upload completes
- Displays: file thumbnail/icon, file name, file size (same as shown during upload)
- Read-only link field with copy button — clicking copies to clipboard and shows brief "Copied!" toast
- Additional: "Copy link" primary button, "Send via email" secondary (Pro), expiry label
- Card remains visible even if user navigates away and back (session-persistent)
- Mobile: Copy action opens native share sheet where available

### Delivery Grid Card
**Behavior:**
- Visual card with file thumbnail area + metadata below
- Click on card → navigates to /deliveries/{id} detail page
- Actions (icon buttons, no text labels on desktop grid):
  - Copy link: copies immediately, shows "Copied!" toast
  - Extend: extends expiry by 24h, shows "Extended by 24h" toast
  - Delete: shows confirmation dialog "Delete this delivery? Recipients will lose access."
- Hover on card: slight lift (translateY(-2px)), shadow deepens, action icons appear

### Download Page
**Behavior:**
- Standalone page, no site navigation visible
- **Ad gate (Free-tier senders only):** Interstitial countdown overlay appears first (3-5s): "Your download will start in 3s..." with ad displayed alongside. After countdown, ad remains in a contained panel below the file card. Every subsequent download of the same link skips the countdown (ad panel remains). Pro/Studio sender deliveries: no ads ever — clean page.
- **Clean trial:** New accounts get 3-5 clean (ad-free) deliveries before the ad gate activates for their Free-tier deliveries.
- Sender's branding (logo) centered at top — Pro/Studio only; Free senders see generic ZapDrop branding
- Optional personal message from sender displayed below logo
- File card shows: file icon (color-coded by type) + name + size
- **Download-count display:** "3 of 5 downloads remaining" shown below download button. When cap reached, button becomes disabled: "Download limit reached. Ask the sender to re-send."
- "Download" primary button (shows total size)
- 🔒 "End-to-end encrypted" badge below button (subtle, not prominent). Tooltip: "Encrypted on the sender's device. ZapDrop cannot read this file."
- **Report button:** "Report this delivery" link at page bottom. Opens simple form: reason dropdown (Spam/Malware/Illegal content/Other) + optional message + Submit. On submit, delivery is locked and sender notified.
- On download click: begins immediately (after ad countdown for Free-tier). Browser native download.
- If password protected: show password field before file card (server-side gate — server withholds file until password verified)
- If paid delivery: show payment prompt (VietQR code) before file access. Unpaid → payment UI; paid → file card + download.
- Expired: "This delivery has expired" (410 page) with clock icon. "Request re-send" button if sender allows.

### Notification Toast
**Behavior:**
- Appears top-right (desktop) or top-center (mobile)
- 3 variants: success (green), error (red), info (blue)
- Auto-dismiss: success/info after 4s, error stays until dismissed
- Slide in (translateY), fade out
- Max 3 toasts visible simultaneously (queue additional)

---

## State Patterns

### Empty States

| Screen | Message | Visual |
|--------|---------|--------|
| Dashboard (no deliveries) | "No deliveries yet — your first share is one click away" | Upload icon illustration centered, subtle, in brand colors |
| Download page (expired) | "This delivery has expired. Ask the sender to re-send." | Clock icon, gray tone, "Request re-send" button |
| Account (not logged in) | Redirect to login | Full-page auth gate |

### Loading States

| Context | Pattern | Detail |
|---------|---------|--------|
| Upload progress | Animated progress bar | Shows % + speed + ETA + file size |
| Dashboard load | Skeleton cards | 4 gray placeholder cards with shimmer animation (300ms wave) |
| Delivery detail load | Skeleton block | Single large skeleton matching detail card layout |
| Download page load | Centered spinner | Simple rotating ring, brand accent color |
| Page transition | Instant (SSR) | Blazor SSR renders immediately, no loading state needed |

### Error States

| Error | Response | Recovery |
|-------|----------|----------|
| Upload fails mid-way | Red toast: "Upload failed. Check your connection." | "Retry" button in toast, clears failed file, user can re-drop |
| Network lost during upload | Toast: "Connection lost. Retrying..." | Auto-retry for 30s, then "Upload paused" with manual retry |
| Expired link accessed | Polite error page with explanation | "Request re-send" button sends notification to original sender |
| Invalid share code | "This link doesn't exist" message | "Go to ZapDrop" link back to home |
| Network lost during download | Toast: "Download interrupted. Check your connection." | "Retry" button resumes from last verified chunk, no full restart |
| Payment pending | Payment prompt stays visible with "Waiting for payment..." status + timer | Polls webhook; auto-advances on confirmation. Manual refresh available. |
| Payment failed / timeout | "Payment not received. The delivery is still available." | "Try again" button re-shows VietQR code. No penalty for timeout. |

### Success States

| Action | Confirmation |
|--------|-------------|
| Upload complete | Progress bar fills, "Link copied to clipboard" text appears, share card shows |
| Link copied | Brief "Copied!" tooltip near copy button, or mobile native toast |
| Extend expiry | Toast: "Extended by 24h" + card badge updates visually |
| Download started | Browser download dialog, or "Your download will start shortly" if delayed |
| Profile updated | Toast: "Profile updated" + page refreshes with new data |

---

## Interaction Primitives

### Input Patterns
- **File selection:** OS native file dialog (opened by click on drop zone or button)
- **Drag and drop:** Full-page drop zone, visual feedback on drag-over (border highlights, background tints)
- **Text input:** Single-line fields with clear label + placeholder. No floating labels (keep simple).
- **Search:** Simple text input with debounced filtering (300ms delay after keystroke)
- **Password:** Single field with eye toggle for visibility

### Feedback Loops
- **Copy action:** Immediate feedback ("Copied!" tooltip) + clipboard write
- **Upload progress:** Real-time (refreshed every 200ms via JS timer)
- **Delete action:** Confirmation dialog required before destructive action
- **Extend action:** Optimistic — badge updates immediately, server call in background. Toast on success/failure.

### Response Times
- **Page navigation:** < 200ms (SSR, server-rendered)
- **Upload progress updates:** Every 200ms
- **Copy to clipboard:** < 50ms
- **Search filtering:** 300ms debounce, then < 100ms filter
- **Toast auto-dismiss:** 4s (success/info), manual (error)

### Gestures (Mobile)
- **Tap:** Primary action (download, copy link)
- **Long press on card:** Select for bulk action (future)
- **Swipe left on card:** Reveal delete action (future)
- **Pull down:** Refresh dashboard (future)

---

## Accessibility Floor

### Behavioral Requirements
- All actions keyboard-navigable (Tab, Enter, Escape)
- Focus indicators visible on all interactive elements (focus ring: `--border-focus`)
- Drop zone is keyboard-activatable (Enter/Space opens file picker)
- Toast notifications: role="alert", live region
- Download page: skip-to-content link for screen readers
- Share link copy: announce "Link copied" via aria-live region

### Screen Reader Notes
- Upload progress: aria-valuenow, aria-valuemin, aria-valuemax on progress bar
- Status badges: include aria-label ("Status: active", "Status: expiring")
- Icon buttons: always include aria-label (e.g., "Copy delivery link", "Delete delivery")
- Empty states: role="status" with aria-live="polite"

### Motion Sensitivity
- Respect prefers-reduced-motion: disable card hover lift, toast slide-in, skeleton shimmer
- All animations < 300ms and non-looping (except progress bar)
- No parallax, no auto-scroll, no rotating elements

---

## Key Flows

### Flow 1: Huy sends a video project (happy path)

1. Huy opens ZapDrop → lands on upload page (/)
2. Drop zone is centered, prominent, clear CTA
3. Huy drags "Project_Alpha.mp4" (4.2 GB) onto the drop zone
4. Border highlights, file preview appears instantly below
5. Upload starts automatically — progress bar + "4.2 GB · 45.3 MB/s · ~1m 30s"
6. During final stage: brief "Encrypting..." label appears
7. Upload completes → progress bar fills → share link card slides in
8. Link is pre-copied to clipboard; "Copied!" toast appears
9. Huy pastes link into Zalo chat with client
10. **Climax:** Link is in the chat within 2 minutes. No registration, no compose flow, no prompts.

### Flow 2: Trang's client downloads wedding photos

1. Client receives Zalo message from Trang: "Your photos are ready! [link]"
2. Clicks link → opens /s/aBc123 in browser
3. Sees Trang's studio logo at top, personal message: "Cảm ơn bạn đã tin tưởng! 🎉"
4. Below: "Wedding_Gallery.zip" with 2.8 GB size, download button
5. Clicks "Download All" → browser download starts
6. **Climax:** Files are downloading. The page is clean, professional, fast. Client feels "this studio is legit."

### Flow 3: Huy checks delivery status

1. Huy taps "Deliveries" in mobile bottom tab
2. Grid view shows all past deliveries as visual cards
3. Each card shows: file thumbnail area, file name, status badge (green), download count ("12")
4. He sees the amber "Expiring soon" badge on an old delivery (11h left)
5. Taps the extend button → "Extended by 24h" toast appears → badge returns to green
6. **Climax:** One tap to extend. No page load, no navigation. Done in 3 seconds.

### Flow 4: Trang sends password-protected delivery (Studio)

1. Trang uploads "Client_Contract.pdf" (normally, no compose flow)
2. But this is sensitive — she checks "Password protect" checkbox before uploading
3. A single password field appears below: "Set a password for this delivery"
4. She enters password, uploads
5. Link generated as usual
6. Client opens link → sees password field instead of download button (server-side gate)
7. Enters correct password → server releases Manifest → file card + download button reveal
8. **Climax:** Simple extra step for sensitive files. Zero friction for protect-or-not decision.

### Flow 5: Free tier user hits limit

1. Huy tries to upload "4K_Project.mov" (6.2 GB)
2. Drop zone accepts the file but shows inline message below:
3. "Files over 5 GB require Pro. This file is 6.2 GB."
4. Two buttons: "Upgrade to Pro" (primary) and "Cancel" (secondary)
5. No modal, no popup — just a clear, honest message in context
6. **Anti-MLM:** No referral upsell. No "share to unlock." Just a direct choice.

### Flow 6: Trust verification (power user)

1. Huy is on a download page (as recipient or viewing sent delivery)
2. Notices 🔒 "End-to-end encrypted" badge
3. Curious, clicks "Verify encryption" link at page bottom
4. Expandable section slides open showing:
   - "Your files were encrypted with AES-256-GCM before upload."
   - Content hash: `a3f8b21e...`
   - "ZapDrop cannot read your files. Only you hold the decryption key (embedded in the link)."
5. For deep verification: "Open browser DevTools (F12) and paste: [code snippet]"
6. **Climax:** Technical user satisfied. Non-technical user ignores it. Both happy.

### Flow 7: Free-tier recipient sees ads (clean trial exhausted)

1. New Free user Linh sends her first delivery → recipient gets clean page (trial delivery #1 of 5)
2. After 5 deliveries, Linh sends another → recipient hits the ad gate
3. Recipient opens link → sees interstitial countdown: "Your download will start in 3s..." with ad panel
4. After countdown, ad remains in a contained panel below the file card — not covering the download
5. Same link re-opened later → no countdown, just the ad panel below
6. **Climax:** Ads are present but never block access. The experience is "ad-supported," not "ad-walled."

### Flow 8: Trang charges for a wedding gallery (Studio + VietQR)

1. Trang uploads "Wedding_Full_Set.zip" (12 GB) — Studio tier, no size cap
2. She toggles "Require payment" and sets price: 150,000 VND
3. Upload completes, link generated as usual
4. Client opens link → sees payment prompt: "This delivery requires payment — 150,000 VND" with VietQR code
5. Client scans QR with banking app, pays
6. Payment webhook confirms → page refreshes → file card + download button appear
7. Trang sees "Paid" status on her dashboard for this delivery
8. **Climax:** Payment is local (VietQR), instant, no Stripe account needed. Client pays the way they already pay.

### Flow 9: Recipient reports a malicious link

1. Recipient opens a suspicious delivery link
2. Scrolls to bottom → clicks "Report this delivery"
3. Simple form appears: reason dropdown (Spam/Malware/Illegal content/Other) + optional message
4. Submits → "Thank you. This delivery has been reported and will be reviewed."
5. Delivery is immediately locked (server-side takedown); sender notified
6. **Climax:** One click to report. No account required. Firefox Send's fatal omission is ZapDrop's day-1 feature.
