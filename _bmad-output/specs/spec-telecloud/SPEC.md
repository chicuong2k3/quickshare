---
id: SPEC-telecloud
companions:
  - architecture.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# TeleCloud — Telegram-backed file storage service

## Why

TeleCloud captures the opportunity to use Telegram's free, unlimited cloud storage as a file hosting backend. Instead of paying for S3 or managing local disk, files are uploaded to a Telegram channel and their metadata is tracked in PostgreSQL. This gives us an HTTP API for upload, download (with HTTP range support), listing, search, folder management, and deletion — all backed by Telegram's infrastructure. The service targets personal or small-team use where Telegram's limits (2 GB per file) are acceptable.

## Capabilities

- **CAP-1**
  - **intent:** Upload files to a Telegram storage channel via multipart/form-data, organized into folders by Folder ID.
  - **success:** File metadata is persisted in PostgreSQL with the correct FolderId FK. The file is retrievable by its UUID id via CAP-3 and CAP-5.

- **CAP-2**
  - **intent:** List files with optional folder filter (by FolderId), full-text search (filename + folder path), multi-column sort (repeated `?sort=field dir` params, ASC default), and pagination.
  - **success:** Returns paged results (items + total count) ordered per sort params. Invalid sort property names return 400 with a descriptive message. Search matches both filename and folder path case-insensitively.

- **CAP-3**
  - **intent:** Download files from Telegram as full downloads or partial range requests (RFC 7233 HTTP 206), with optional inline streaming via `?stream=true`.
  - **success:** Streams correct byte ranges for both full (200) and partial (206) requests. TeleCloud metadata drives Content-Type and Content-Disposition headers.

- **CAP-4**
  - **intent:** Delete files from the Telegram channel and remove their PostgreSQL metadata record.
  - **success:** TeleCloud file message is removed from the Telegram channel and the DB record is deleted. Returns 204 on success, 404 if the file does not exist.

- **CAP-5**
  - **intent:** Retrieve metadata for a single file by id without initiating a download.
  - **success:** Returns full metadata (filename, size, content-type, folderId, folderPath, tags, created-at) for the given id. Returns 404 if not found.

- **CAP-6**
  - **intent:** All file and folder operations flow through Wolverine message handlers injected via primary constructors. Endpoints dispatch commands/queries via `IMessageBus.InvokeAsync`. Handlers return `LightResults.Result` or `Result<T>`.
  - **success:** Each handler is independently testable. Wolverine inbox/outbox with PostgreSQL persistence ensures automatic retry on transient Telegram API failures. `AutoApplyTransactions()` wraps every handler in an EF Core transaction.

- **CAP-7**
  - **intent:** CRUD API for folders. Create folders with unique paths, list folders with sort/pagination, get folder info, rename folders, and delete folders (only when empty).
  - **success:** Folder CRUD operations return `FolderInfoResponse` (Id, Path, CreatedAt). Delete returns 409 Conflict if the folder still contains files. Duplicate path creation returns 409 Conflict.

## Constraints

- Must use Telegram as the file storage backend via WTelegramClient 4.x (not local disk or S3).
- Must use PostgreSQL via EF Core 10.x with Npgsql for metadata storage and Wolverine inbox/outbox.
- Must use FastEndpoints 8.x (not WolverineHttp) as the HTTP endpoint framework. Endpoints extend `Endpoint<TReq,TRes>`.
- Must use WolverineFx 5.x as the command bus with `PersistMessagesWithPostgresql` + `AutoApplyTransactions`.
- Telegram authentication requires `api_id`, `api_hash`, `phone_number`, configured via `TeleCloudOptions` section. Session persists via `WTelegram.session` file.
- Endpoints must be thin delegates: extract transport data (form fields, headers, query params) and dispatch via `IMessageBus`. No business logic, range parsing, or 404 wrapping in endpoints.
- Must use functional error handling (`LightResults.Result`) for expected errors. `GlobalExceptionHandler` catches unhandled exceptions as 500 ProblemDetails.
- Sort params must be validated before use via source-generated `TryValidateSorts`. Invalid sort property → 400 BadRequest, not 500.

## Non-goals

- No Telegram bot API usage — uses user-account authentication.
- No multi-user or multi-tenant support.
- No file encryption at rest or in transit beyond what Telegram provides.
- No automatic channel or folder creation — both must be pre-configured / created via API.
- No thumbnail generation, preview, or media transcoding.
- No soft-delete — file deletion removes from both Telegram and PostgreSQL.

## Success signal

A file uploaded through POST /api/files to a folder is downloadable by its id via GET /api/files/{id}/download (with HTTP range support), its metadata is queryable through GET /api/files and GET /api/files/{id}, and it can be deleted via DELETE /api/files/{id} — all backed by Telegram storage and PostgreSQL metadata. Folders are manageable through /api/folders CRUD. Swagger UI documents all 10 endpoints. Invalid inputs (wrong sort fields, missing files, duplicate folder paths) return appropriate 400/404/409 responses, not 500s.

## Assumptions

- Telegram storage channel is pre-configured (numeric ID or @username in TeleCloud settings).
- Single-user Telegram auth via `LoginUserIfNeeded`. Session file persists across restarts.
- Telegram's 2 GB per-file limit and channel message rate limits are acceptable.

## Open Questions

- Should tags on files be exposed as a filterable/searchable field in the list endpoint?
- Should file deletion be soft-delete (mark deleted in DB, keep in Telegram) as an option?
- Should we add a batch upload endpoint for multiple files?
