---
id: SPEC-quicksharecloud
companions:
  - ux-flow.md
  - server-changes.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# QuickShareCloud — Quick file sharing with auto-expiry

## Why

QuickShareCloud captures the opportunity to be the fastest way to share temporary files online. Current options (WeTransfer, Google Drive) require too many steps — login, fill forms, wait for processing. QuickShareCloud is: drag a file, get a link in your clipboard in under 2 seconds. No login, no fields, no friction. Targets developers (API access), designers (quick asset sharing), office workers (document exchange), and students (file transfer). Competes on speed, weight, and developer ergonomics.

## Capabilities

- **CAP-1**
  - **intent:** User can upload a file by dragging it onto the page or pasting an image (Ctrl+V). Upload starts immediately with no form fields.
  - **success:** File uploads in under 2 seconds (for files under 100 MB on typical broadband) and the share link appears.

- **CAP-2**
  - **intent:** After successful upload, the share link is automatically copied to the clipboard and displayed prominently.
  - **success:** Link is in system clipboard without manual copy, confirmed by a visual indicator (checkmark or toast).

- **CAP-3**
  - **intent:** Each share link generates a QR code the user can show or screenshot for others to scan.
  - **success:** QR code is displayed alongside the link and is scannable by any standard QR reader app.

- **CAP-4**
  - **intent:** Uploaded files expire after a chosen duration (24 hours or 7 days). Expired files are inaccessible and cleaned up.
  - **success:** Accessing an expired link shows a clear expiration message with the filename and expiry time. Expired files are removed from Telegram and DB by the cleanup job.

- **CAP-5**
  - **intent:** The entire upload flow works without login or registration. Uploader is anonymous.
  - **success:** A clean-session browser can drag a file, upload it, and get a shareable link in under 3 seconds total.

- **CAP-6**
  - **intent:** The share link opens a clean download page showing filename, size, and a one-click download button.
  - **success:** Recipient can download the file with one click. Page renders without login. Expired links show an expiration state.

- **CAP-7**
  - **intent:** Developers can upload temp files programmatically via the QuickShareCloud API (`POST /api/temp`), getting back a share link and expiry time.
  - **success:** A `curl -F file=@document.pdf -F expiry=24h https://quicksharecloud.example/api/temp` returns `{ "link": "...", "expiresAt": "..." }` with a 201 status.

## Constraints

- Must be a Blazor .NET 10 Static SSR application using RizzyUI with ArcticTheme as default theme. No WebAssembly — RizzyUI requires SSR.
- Must use the existing TeleCloud API backend (Telegram storage, PostgreSQL metadata, Wolverine/FastEndpoints) for file operations.
- All temp share files must have a server-enforced expiry timestamp. The backend — not the client — enforces access cutoff.
- Upload size limit is 2 GB per file (inherited from Telegram backend constraint).
- Links must be unguessable (opaque UUID-based, not sequential IDs).
- QR code generation must happen client-side in the browser via JS interop to avoid server dependency.
- RizzyUI interactivity is via Alpine.js (injected by RzHeadOutlet). No Blazor InteractiveServer components required for this feature.

## Non-goals

- No user accounts, login, or registration system.
- No permanent file storage — all temp shares expire.
- No file preview, thumbnail generation, or media transcoding.
- No multi-file batch upload in a single share link.
- No recipient email notification or sharing-to-email flow.
- No copyright enforcement or DMCA compliance system.

## Success signal

A person on any modern browser visits quicksharecloud.example, drags a file or pastes an image, and within 3 seconds has a shareable link in their clipboard. The link recipient sees a clean download page with filename, size, and a one-click download. Expired links show a clear expiration state, not a broken page. A developer achieves the same via `curl` in one line.

## Assumptions

- Assumed the Blazor SSR app is a separate project from the existing TeleCloud API, deployed as a standalone static-rendered site calling the API backend.
- Assumed ArcticTheme (light, clean, professional) as default — suitable for all four target audiences.
- Assumed Tailwind CSS 4.x build pipeline is set up per RizzyUI docs (NPM tailwindcss CLI + @jalexsocial/rizzyui package).
- Assumed a server-side cleanup hosted service in the TeleCloud API handles expiry enforcement on a timer, not an external cron job.

## Open Questions

- Should the expiry duration selector be on the upload page (before upload) or shown after the link is generated (changeable post-upload)?
- Should download count tracking be exposed on the uploader's side (e.g., "downloaded 3 times")?
