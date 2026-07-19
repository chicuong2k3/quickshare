---
name: ZapDrop
type: design
purpose: visual-identity
status: final
created: 2026-07-19
updated: 2026-07-19
finalized: 2026-07-19
sources:
  - "_bmad-output/planning-artifacts/prds/prd-TeleCloud-2026-07-19/prd.md"
  - "_bmad-output/architecture/architecture-TeleCloud-owned-storage-2026-07-19/ARCHITECTURE-SPINE.md"
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/EXPERIENCE.md"
companions:
  - "_bmad-output/planning-artifacts/ux-designs/ux-TeleCloud-2026-07-19/EXPERIENCE.md"
---

# DESIGN — ZapDrop

*Visual identity spine. How it looks. Formerly TeleCloud — renamed 2026-07-19 per PRD pivot.*

---

## Brand & Style

### Personality
- **Speed** — Clean lines, generous whitespace, no visual clutter. The interface feels as fast as the transfer.
- **Simplicity** — Single-purpose pages. One primary action per screen. Nothing unnecessary.
- **Professional** — Polished but not corporate. More refined than WeTransfer, warmer than MASV.
- **Minimalist** — Content-forward. Typography-driven. Decorative elements serve function.
- **Friendly** — Human warmth in microcopy and spacing. Not cold or technical-feeling.

### Positioning
- **Honest freemium** — Free tier is genuinely useful and never feels like a trap. Upgrade prompts are contextual, not naggy. Ads are subtle and below content.
- **Ổn định, không bị khóa** — Stable, not locked. Communicate reliability and freedom.
- **Anti-MLM** — No referral schemes, no gamification. The product sells on its merits.

### Visual Vibe Keywords
Clean · Airy · Purposeful · Fast · Trustworthy · Vietnamese craft · Professional-grade

### Anti-References (what ZapDrop is NOT)
- NOT playful/casual like WeTransfer
- NOT technical/corporate like MASV
- NOT cluttered/utilitarian
- NOT gamified or MLM-adjacent
- NOT skeuomorphic or heavy-shadowed
- NOT a Telegram front-end (storage is owned, not third-party)

---

## Colors

### Brand Palette (inspired by WeTransfer's simplicity + Vietnamese warmth)

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `--brand` | #1A1A2E (deep navy) | #E8E8F0 (off-white) | Primary brand color, logo, key CTAs |
| `--brand-subtle` | #F0F0F5 | #2A2A3E | Brand backgrounds, hover states |
| `--accent` | #4F6CF7 (vibrant blue) | #6B8AFF (lighter blue) | Primary actions, links, interactive elements |
| `--accent-subtle` | #EEF1FF | #2D3566 | Accent backgrounds, selected states |

### Semantic Palette

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `--bg` | #FCFCFA (warm white) | #121218 (near-black) | Page backgrounds |
| `--bg-card` | #FFFFFF | #1A1A22 | Card backgrounds |
| `--fg` | #1A1A2E | #E8E8F0 | Primary text |
| `--fg-muted` | #8A8A9E | #8A8A9E | Secondary text, metadata |
| `--border` | #E2E2EA | #2E2E3E | Dividers, borders |
| `--border-focus` | #4F6CF7 | #6B8AFF | Focus rings |
| `--success` | #22C55E | #34D975 | Download complete, active status |
| `--warning` | #F59E0B | #FBBF24 | Expiring soon (<24h) |
| `--danger` | #EF4444 | #F87171 | Expired, errors, destructive actions |

### Usage Rules
- Use `--accent` sparingly — one primary action per screen
- Status badges use full saturation in light mode, slightly lighter in dark mode
- Background should always feel warm (tinted with subtle #FFFDF5 undertone), never pure cold gray

---

## Typography

### Font Family
- **Primary:** Inter (sans-serif) — crisp, readable at all sizes, excellent Vietnamese diacritic support
- **Monospace:** JetBrains Mono — for code snippets, file hashes, technical verification
- **Fallback:** system-ui, -apple-system, sans-serif

### Type Scale

| Token | Size | Weight | Line Height | Usage |
|-------|------|--------|-------------|-------|
| `--text-xs` | 0.75rem (12px) | 400 | 1.5 | Labels, metadata, timestamps |
| `--text-sm` | 0.875rem (14px) | 400 | 1.5 | Body text, descriptions |
| `--text-base` | 1rem (16px) | 400 | 1.6 | Default body |
| `--text-lg` | 1.125rem (18px) | 500 | 1.5 | Section headings |
| `--text-xl` | 1.25rem (20px) | 600 | 1.4 | Card titles |
| `--text-2xl` | 1.5rem (24px) | 600 | 1.3 | Page headings |
| `--text-3xl` | 1.875rem (30px) | 700 | 1.2 | Hero headings |

### Usage Rules
- No font-weight below 400 (keep crisp on screens)
- Headings use tight line-height (1.2–1.3) for impact
- Body text uses comfortable line-height (1.5–1.6) for readability
- File names use `--text-base`, file sizes use `--text-sm` with `--fg-muted`
- Brand name always set in medium weight (500), never bold

---

## Layout & Spacing

### Grid
- **Max container width:** 1200px (desktop)
- **Content column:** 640px (upload page), 800px (dashboard), 600px (download page)
- **Gutter:** 24px (desktop), 16px (tablet), 12px (mobile)

### Spacing Scale

| Token | Value | Usage |
|-------|-------|-------|
| `--space-1` | 4px | Tight spacing, icon gaps |
| `--space-2` | 8px | Inner padding (small) |
| `--space-3` | 12px | Button padding, chip gaps |
| `--space-4` | 16px | Card padding, section gaps |
| `--space-6` | 24px | Section spacing, modal padding |
| `--space-8` | 32px | Component clusters |
| `--space-12` | 48px | Page section separation |
| `--space-16` | 64px | Major page breaks |

### Responsive Breakpoints
- **Mobile:** < 640px
- **Tablet:** 640px – 1024px
- **Desktop:** > 1024px

### Layout Principles
- Upload page: single-column, centered, generous top padding
- Dashboard: two-column at desktop (sidebar + content), single-column mobile
- Download page: single-column, centered, minimal chrome
- Always maintain `--space-12` between content and footer

---

## Elevation & Depth

### Shadows (Light Mode)

| Token | Value | Usage |
|-------|-------|-------|
| `--shadow-sm` | 0 1px 2px rgba(0,0,0,0.04) | Cards, subtle separation |
| `--shadow-md` | 0 4px 12px rgba(0,0,0,0.06) | Dropdowns, elevated cards |
| `--shadow-lg` | 0 8px 24px rgba(0,0,0,0.08) | Modals, tooltips |
| `--shadow-xl` | 0 12px 40px rgba(0,0,0,0.10) | Full-screen overlays |

### Shadows (Dark Mode)
- Same distribution, use rgba(0,0,0,0.3) base with luminous borders instead

### Border Radius

| Token | Value | Usage |
|-------|-------|-------|
| `--radius-sm` | 6px | Badges, small indicators |
| `--radius-md` | 10px | Buttons, input fields |
| `--radius-lg` | 14px | Cards, modals |
| `--radius-xl` | 20px | Large containers, hero areas |
| `--radius-full` | 9999px | Avatars, pills, status dots |

### Layering
- Dropdown: z-50
- Sticky header: z-40
- Modal overlay: z-50
- Toast/notifications: z-60

---

## Components

### 1. Upload Drop Zone
- Large dashed-border rectangle at center of page
- Border color: `--border` (2px dashed)
- Border radius: `--radius-lg`
- Hover state: border shifts to `--accent`, background shifts to `--accent-subtle` with transition (200ms ease)
- Inner content: upload icon SVG (24x24), "Drag files here or click to browse", file types hint below
- Active/drag-over state: solid border, accent background, slight scale bump (transform: scale(1.01))
- Click opens OS file picker (accept: any)
- **Mobile:** Replace with rounded "Select files" button (same accent color), full-width, 56px height

### 2. Upload Progress
- Horizontal bar, full-width below drop zone
- Bar height: 4px
- Background: `--border`, fill: `--accent` (animated with smooth transition)
- Text below: "Uploading — 45% · 12.4 MB / 27.5 MB · 2.1 MB/s · ~6s remaining"
- Color: `--fg-muted`, size: `--text-xs`
- If encrypting: display "Encrypting..." label during final stage before completion
- Completion: bar fills to 100%, text changes to "Link copied to clipboard" with checkmark icon

### 3. Share Link Card
- Card container: white background, `--shadow-sm`, `--radius-lg`, padding `--space-6`
- Displays: file icon (based on type), file name, file size
- Link input: read-only text field with copy button
  - Field: `--bg` background, `--border` border, `--radius-md`, monospace font (13px)
  - Copy button: `--accent` background, white icon, hover brightens 5%
  - On copy: brief "Copied!" tooltip (1.5s fade out)
- Additional actions: "Copy link", "Send via email" (Pro), "Expires in X days" label
- Mobile: Full-width card, stacked layout

### 4. Delivery Grid Card (Dashboard)
- Card: `--bg-card`, `--shadow-sm`, `--radius-lg`, hover lift (+2px y, shadow-md transition)
- Thumbnail area: top section, 3:2 aspect ratio, file type icon centered on subtle gradient background
- Info section:
  - File name: `--text-base`, truncate at 1 line
  - Status badge: pill shape, `--radius-full`, 4px horizontal padding. Green for active, amber for <24h, gray for expired
  - Download count: eye icon + number, `--text-sm`, `--fg-muted`
  - Days remaining: clock icon + "3 days left", `--text-sm`, `--fg-muted`
- Actions row: Copy link icon-button (primary accent), Extend (+24h) icon-button (ghost), Delete icon-button (ghost, red on hover)
- Mobile: 2-column grid (tablet), 1-column (phone)

### 5. Download Page
- Single-purpose layout, centered
- Top: sender's logo/branding (centered, max 180px). Pro/Studio only.
- Message: italicized sender note, centered, `--text-lg`, `--fg-muted`
- File card: `--bg-card`, `--shadow-md`, `--radius-lg`. File icon + name + size in a row
- Download All button: full-width, `--accent` background, white text, `--radius-md`, 56px height. "Download (1.2 GB)"
- Download count: "3 of 5 downloads remaining" below button, `--text-sm`, `--fg-muted`
- Encryption badge: small 🔒 "End-to-end encrypted" below button area, `--text-sm`, `--fg-muted`
- Verify link: "Verify encryption" text link, opens expandable section with hash + explanation
- Report link: "Report this delivery" text link at page bottom, `--text-xs`, `--fg-muted`. Opens simple report form (reason dropdown + submit).
- Free-tier sender: interstitial ad countdown appears before download (3-5s overlay, "Your download will start in 3s..." with ad alongside). After countdown, ad remains in a contained panel below the file card. Pro/Studio sender: NO ads, clean page.
- Footer: "Powered by ZapDrop" (subtle, `--text-xs`, `--fg-muted`)

### 6. Bottom Tab Bar (Mobile)
- Fixed bottom, background: `--bg-card`, top border: `--border`
- Height: 56px
- Tabs: Upload, Deliveries, Account (3 tabs maximum — simplicity)
- Active tab: `--accent` icon + label, inactive: `--fg-muted` icon + label
- Upload tab always center, slightly elevated (primary action)

### 7. Status Badges
- Pill shape, `--radius-full`
- Active: green background (`--success` at 12% opacity), green text
- Expiring (<24h): amber background, amber text
- Expired: gray background (`--border` at 50% opacity), `--fg-muted` text
- Size: `--text-xs`, 6px vertical padding, 10px horizontal

### 8. Notification Toast
- Appears top-right (desktop) or top-center (mobile)
- `--shadow-lg`, `--radius-md`, `--bg-card` background
- 3 variants: success (green left border), error (red), info (blue)
- Icon + message + optional dismiss "×" button
- Auto-dismiss: 4 seconds (success/info), persistent (error)
- Slide-in animation (translateY -20px → 0, 300ms ease)

---

## Do's and Don'ts

### Do
- ✅ Use generous whitespace — let content breathe
- ✅ One primary action per page — make the decision obvious
- ✅ Use accent color sparingly — it should draw the eye to the action
- ✅ Keep file names visible and truncate gracefully with ellipsis
- ✅ Show meaningful progress — speed, ETA, percentage
- ✅ Use icons consistently — same stroke width (1.5px), same visual weight
- ✅ Communicate encryption visually (🔒 badge) but don't explain unless asked

### Don't
- ❌ Don't use heavy shadows or depth — keep flat and clean
- ❌ Don't add unnecessary UI chrome — no sidebars, no mega-menus
- ❌ Don't bury the primary action — download/copy should be immediately obvious
- ❌ Don't use alert banners for upsells — upgrade prompts should be contextual and subtle
- ❌ Don't show technical jargon to non-technical users — keep the default experience plain-language
- ❌ Don't animate aggressively — subtle transitions only (200-300ms), no bounce or splash effects
