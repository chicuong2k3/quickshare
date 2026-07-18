---
id: SPEC-telecloud
companions:
  - architecture.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# TeleCloud — Telegram-backed file storage service

## Why

TeleCloud captures the opportunity to use Telegram's free, unlimited cloud storage as a file hosting backend, combined with WeTransfer-style sharing features. The app supports multi-user access via social login (Google, TikTok, Facebook, GitHub) with per-user file ownership and isolation, while all files are stored through a single shared Telegram account/channel. Instead of paying for S3 or managing local disk, files are uploaded to a Telegram channel and their metadata is tracked in PostgreSQL. This gives us a feature-rich file sharing platform with password-protected transfers, expiration dates, branded transfer pages, paid downloads via Stripe, and guest file request links — all backed by Telegram's infrastructure.

## Capabilities

- **CAP-1**
  - **intent:** Upload files to a Telegram storage channel via multipart/form-data, organized into folders by Folder ID.
  - **success:** File metadata is persisted in PostgreSQL with the correct FolderId FK. The file is retrievable by its UUID id via CAP-3 and CAP-5.

- **CAP-2**
  - **intent:** List files for the authenticated user with optional folder filter (by FolderId), full-text search (filename + folder path), multi-column sort (repeated `?sort=field dir` params, ASC default), and pagination.
  - **success:** Returns paged results (items + total count) ordered per sort params, scoped to the current user's files only. Invalid sort property names return 400 with a descriptive message. Search matches both filename and folder path case-insensitively.

- **CAP-3**
  - **intent:** Download files from Telegram as full downloads or partial range requests (RFC 7233 HTTP 206), with optional inline streaming via `?stream=true`.
  - **success:** Streams correct byte ranges for both full (200) and partial (206) requests. TeleCloud metadata drives Content-Type and Content-Disposition headers. Password-protected files verify password before streaming (CAP-8).

- **CAP-4**
  - **intent:** Delete files from the Telegram channel and remove their PostgreSQL metadata record. Restricted to file owner.
  - **success:** TeleCloud file message is removed from the Telegram channel and the DB record is deleted. Returns 204 on success, 404 if the file does not exist, 403 if requesting user is not the owner.

- **CAP-5**
  - **intent:** Retrieve metadata for a single file by id without initiating a download.
  - **success:** Returns full metadata (filename, size, content-type, folderId, folderPath, tags, created-at, expiration, password-protected flag) for the given id. Returns 404 if not found.

- **CAP-6**
  - **intent:** All file and folder operations flow through Wolverine message handlers injected via primary constructors. Endpoints dispatch commands/queries via `IMessageBus.InvokeAsync`. Handlers return `LightResults.Result` or `Result<T>`.
  - **success:** Each handler is independently testable. Wolverine inbox/outbox with PostgreSQL persistence ensures automatic retry on transient Telegram API failures. `AutoApplyTransactions()` wraps every handler in an EF Core transaction.

- **CAP-7**
  - **intent:** CRUD API for folders. Create folders with unique paths, list folders with sort/pagination, get folder info, rename folders, and delete folders (only when empty).
  - **success:** Folder CRUD operations return `FolderInfoResponse` (Id, Path, CreatedAt). Delete returns 409 Conflict if the folder still contains files. Duplicate path creation returns 409 Conflict.

- **CAP-8**
  - **intent:** Set an optional password on file transfers. Recipient must enter the password before accessing files.
  - **success:** Password-protected transfers prompt for password before download; incorrect password returns 401 Unauthorized; correct password returns 200 with the file content (via CAP-3).

- **CAP-9**
  - **intent:** Set a configurable expiration date/time on any file transfer. Expired transfers become inaccessible.
  - **success:** Transfer with past expiration date returns 410 Gone on download/metadata access. Transfer with future expiration date or no expiration remains fully accessible.

- **CAP-10**
  - **intent:** Customize transfer pages with brand logo, background image/color, and personalized message per transfer.
  - **success:** Transfer recipient-facing page renders with the uploader's custom logo, background, and description text. Branding is per-transfer, not global.

- **CAP-11**
  - **intent:** Generate shareable file request links for guest (unauthenticated) users to upload files to a designated folder.
  - **success:** Unauthenticated user with a valid request link can upload files via the link; files land in the configured target folder. Links can be revoked or set to expire. Revoked/expired links return 410.

- **CAP-12**
  - **intent:** Uploader sets a price for their transfer. Recipient must pay via Stripe before downloading.
  - **success:** Unpaid transfer redirects to Stripe checkout. Successful Stripe payment (confirmed via webhook) unlocks the download. Uploader's Stripe account receives the funds. Paid transfers show payment status in metadata.

- **CAP-13**
  - **intent:** Authenticate users via Google, TikTok, Facebook, and GitHub OAuth 2.0 providers. Users sign in with their existing social accounts without creating separate credentials.
  - **success:** User clicks provider button, completes OAuth Authorization Code flow, and is authenticated with a session. User profile (name, email, avatar) is persisted. OAuth callback returns user to the app with a valid auth session.

## Constraints

- Must use Telegram as the file storage backend via WTelegramClient 4.x (not local disk or S3).
- Must use PostgreSQL via EF Core 10.x with Npgsql for metadata storage and Wolverine inbox/outbox.
- Must use FastEndpoints 8.x (not WolverineHttp) as the HTTP endpoint framework. Endpoints extend `Endpoint<TReq,TRes>`.
- Must use WolverineFx 5.x as the command bus with `PersistMessagesWithPostgresql` + `AutoApplyTransactions`.
- Telegram authentication requires `api_id`, `api_hash`, `phone_number`, configured via `TeleCloudOptions` section. Session persists via `WTelegram.session` file. A single Telegram account serves all users — all files upload through the same Telegram channel. Multi-user isolation is enforced at the application/PostgreSQL layer.
- Endpoints must be thin delegates: extract transport data (form fields, headers, query params) and dispatch via `IMessageBus`. No business logic, range parsing, or 404 wrapping in endpoints.
- Must use functional error handling (`LightResults.Result`) for expected errors. `GlobalExceptionHandler` catches unhandled exceptions as 500 ProblemDetails.
- Sort params must be validated before use via source-generated `TryValidateSorts`. Invalid sort property → 400 BadRequest, not 500.
- All web UI pages must use RizzyUI Razor components (RzButton, RzCard, RzBadge, RzTable, RzAvatar, RzAlert, RzProgress, RzSpinner, RzSeparator, RzDialog, etc.) from [rizzyui.jalex.io](https://rizzyui.jalex.io). No raw HTML button, link, table, card, badge, or progress elements — use the equivalent RizzyUI component. Plain `<div>`, `<span>`, `<p>`, `<header>`, `<footer>`, `<main>`, `<nav>` wrappers are allowed for layout structure. Tailwind classes remain available for spacing, alignment, and custom overrides.
- Paid transfers must integrate with Stripe for payment processing. Stripe keys configurable via `TeleCloudOptions`. Stripe webhook endpoint handles payment confirmation.
- File request upload links must be unique, revocable, and allow optional expiration. Uploaded files via request link are limited to a max file size setting configured per request link.
- OAuth providers (Google, TikTok, Facebook, GitHub) must be configurable via `TeleCloudOptions` with `client_id` and `client_secret` per provider. OAuth callback endpoints follow standard OAuth 2.0 Authorization Code flow.

## Non-goals

- No Telegram bot API usage — uses user-account authentication.
- No file encryption at rest or in transit beyond what Telegram provides.
- No automatic channel or folder creation — both must be pre-configured / created via API.
- No thumbnail generation, preview, or media transcoding.
- No soft-delete — file deletion removes from both Telegram and PostgreSQL.
- No asset (image) preview or gallery generation for customized transfer pages. Uploaded background/logo images are served as-is.

## Success signal

A file uploaded through POST /api/files to a folder is downloadable by its id via GET /api/files/{id}/download (with HTTP range support), its metadata is queryable through GET /api/files and GET /api/files/{id}, and it can be deleted via DELETE /api/files/{id} — all backed by Telegram storage and PostgreSQL metadata. Folders are manageable through /api/folders CRUD. Password-protected files require password verification before download. Expired files return 410 Gone. Branded transfer pages render with custom logos and backgrounds. File request links allow guest uploads to designated folders. Paid transfers process via Stripe and unlock downloads on payment confirmation. Users can authenticate via Google, TikTok, Facebook, or GitHub OAuth. Swagger UI documents all endpoints. Invalid inputs return appropriate 400/404/409/401/410 responses, not 500s.

## Assumptions

- Telegram storage channel is pre-configured (numeric ID or @username in TeleCloud settings).
- A single Telegram account (the platform owner's) is shared across all users for file storage. User-level isolation happens via PostgreSQL OwnerId, not Telegram.
- Telegram's 2 GB per-file limit and channel message rate limits are acceptable.
- File request upload links work without authentication (guest upload) — matching WeTransfer's "share a link, anyone can upload" pattern.
- Transfer branding (CAP-10) is per-transfer, not a global account setting — matching WeTransfer's per-transfer customization panel.
- 2FA is deferred — not needed while Telegram is the single shared storage layer.

## Open Questions

- Should file encryption at rest/in transit be added as a new capability instead of remaining a non-goal? WeTransfer emphasizes encryption as a core differentiator.
- Should multi-user team support (SSO, team branding, shared workspaces) be added? WeTransfer Teams describes this as a key business feature.
- Should anti-malware and phishing detection scanning be added for uploaded files, or is Telegram's built-in scanning sufficient?
- Should tags on files be exposed as a filterable/searchable field in the list endpoint?
- Should file deletion be soft-delete (mark deleted in DB, keep in Telegram) as an option?
- Should we add a batch upload endpoint for multiple files?
