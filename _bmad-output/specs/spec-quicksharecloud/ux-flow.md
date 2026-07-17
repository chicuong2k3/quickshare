# UX Flow — QuickShareCloud

## Upload Page (Main)

### States

| State | When | What the user sees |
|---|---|---|
| **Idle** | Page loaded, no file selected | Drop zone with dashed border, icon, text "Drag & drop a file here or Ctrl+V to paste". Subtle expiry selector: 24h (default) / 7 days. Upload area takes up center screen. |
| **Dragging** | File being dragged over the zone | Drop zone border highlights (color change, glow). Text changes to "Drop to upload". |
| **Uploading** | File dropped or pasted, upload in progress | Progress bar with percentage. Filename shown. Cancel button (X) to abort. |
| **Success** | Upload complete | Link displayed in a large input field (read-only, selected-ready). Checkmark icon confirms "Link copied to clipboard". QR code rendered below the link. Expiry time shown: "Expires: 25 July 2026, 14:30". "Copy link" and "Share" buttons present. |
| **Error** | Upload failed | Error message with reason (size exceeded, network error, server error). "Try again" button to retry. |
| **Expired** | Share link expired | Clean message: "This file has expired." Shows original filename and when it expired. No download button. |

### Input Methods
1. **Drag & drop** — file from OS file manager onto the drop zone.
2. **Ctrl+V paste** — clipboard image is converted to a file and uploaded. Non-image clipboard content shows a tooltip: "No image in clipboard."
3. **Browse button** (secondary) — traditional file input, hidden visually but accessible via keyboard/click on the zone text.

### Expiry Selector
- Two radio-style buttons or a toggle: "24 hours" (default, highlighted) / "7 days"
- Selected before upload. Small, unobtrusive, placed below the drop zone or in a top corner.
- Changing expiry after upload is an open question.

## Download Page (Share Link)

### States

| State | When | What the recipient sees |
|---|---|---|
| **Ready** | Link is valid | Filename, file size (human-readable: "2.3 MB", "1.1 GB"), file type icon. Large "Download" button (primary). Expiry countdown: "Expires in 23 hours". |
| **Downloading** | Download started | Button changes to downloading spinner. Browser handles the actual download. |
| **Expired** | Link past expiry | "This file has expired." Filename and original expiry time shown. No download option. |
| **Not Found** | Invalid link (never existed) | "File not found." Generic 404 page. |

### Layout
- Centered card, minimal chrome. No navigation, no header, no footer. Just the file info and download action.
- File type icon based on MIME type (image, document, archive, generic).

## QR Code
- Generated client-side via JS interop (QRCode.js or similar library, bundled with the app).
- Rendered as inline SVG, no external service calls.
- Sized to be scannable (minimum 200x200px rendered size).

## Theme
- Default: **ArcticTheme** (RizzyUI built-in) — light, clean, professional.
- Dark mode toggle via `RzDarkModeToggle` (RizzyUI built-in, Alpine.js, persists to localStorage).
- All UI uses semantic tokens (bg-primary, text-foreground) so dark mode auto-swaps without markup changes.

## Mobile Responsiveness
- Drop zone adapts to viewport width.
- Link input field is full-width for easy copy-tap on mobile.
- QR code is large enough to scan from another phone screen.
- Download button is full-width on mobile for easy tap.

## Tech Stack (UI)
- Blazor .NET 10 **Static SSR** (no WebAssembly, no InteractiveServer required).
- RizzyUI components + Alpine.js for interactivity (dropdowns, toggles, clipboard copy).
- Tailwind CSS 4.x via CLI build pipeline (`@jalexsocial/rizzyui` NPM package).
- Client-side JS interop for clipboard API, paste handling, QR code generation, and file upload progress.
