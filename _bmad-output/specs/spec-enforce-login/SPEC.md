---
id: SPEC-enforce-login
companions: []
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Require Authentication to Use QuickShareCloud

## Why

A pain to solve. Anonymous uploads enable abuse and spam with no accountability. Without per-user authentication there is no way to rate-limit by account, attribute uploads to a specific user, or enforce plan-tier limits (Free/Starter/Ultimate). This leaves the platform vulnerable to automated abuse and prevents the business model (paid tiers based on usage quotas) from functioning.

## Capabilities

- **CAP-1 — Auth gate for protected pages**
  - **intent:** Any page or route that is not explicitly marked public redirects unauthenticated visitors to `/login` with a 401 status.
  - **success:** A browser visit to `/` (upload) while logged out ends at `/login?returnUrl=/`. A request to `/account` while logged out returns 401.

- **CAP-2 — Upload requires login**
  - **intent:** The upload page and its corresponding API (create temp share) require authentication. Anonymous uploads are rejected.
  - **success:** An unauthenticated POST to the upload API returns 401. The upload drop zone is not rendered for unauthenticated users.

- **CAP-3 — Public download links**
  - **intent:** Recipients can download shared files via `/t/{ShareId}` without any authentication. The download page and its API endpoint remain public.
  - **success:** A user with a share link can download the file in an incognito/private browser session without logging in.

- **CAP-4 — Upload attribution**
  - **intent:** Each temp share record stores the authenticated user's ID (`OwnerId`) at creation time, linking the upload to the uploading user.
  - **success:** After uploading, the share's `OwnerId` column in the database is populated with the user's GUID. Anonymous uploads produce no record.

- **CAP-5 — Server-side auth enforcement**
  - **intent:** FastEndpoints API endpoints enforce authentication via the `[Authorize]` attribute (or equivalent middleware), not relying on client-side UI hiding alone.
  - **success:** A direct HTTP request to any authenticated endpoint without a valid `.AspNetCore.TeleCloud` cookie returns 401.

- **CAP-6 — Per-user rate limits**
  - **intent:** Monthly upload volume (GB) and transfer count are tracked per user. Enforced at upload time against the user's plan tier before the share is created.
  - **success:** A Free-tier user (3 GB/month, 10 transfers) exceeding either limit receives a 429/403 response with a clear message. The limits reset monthly.

- **CAP-7 — Public pages stay public**
  - **intent:** Marketing content — `/pricing`, landing page static content — remains accessible without authentication.
  - **success:** An unauthenticated browser visit to `/pricing` renders the page normally without redirect.

## Constraints

- Reuse the existing `/.AspNetCore.TeleCloud` cookie auth + Google OAuth middleware. Do not replace the auth stack.
- Download links (`/t/{ShareId}`) must remain public — recipients never need accounts.
- Reuse existing PostgreSQL `Users`/`UserLogins` tables. No new data stores or infrastructure.
- Rate limits enforce against the plan tiers already defined in the pricing page: Free = 200 MB/file, 3 GB/month, 10 transfers; Starter = 750 MB/file, 300 GB/month, 10 transfers; Ultimate = 1.5 GB/file, unlimited volume/transfers.
- Update copy on Upload.razor (remove "anonymous · no login required") and Download.razor (remove "No login required").

## Non-goals

- No new social login providers beyond the existing Google OAuth (GitHub/Facebook/TikTok buttons stay as placeholders).
- No email/password registration flow.
- No admin panel for managing users or rate limits.
- No WebAuthn, passkeys, or MFA.
- No audit log beyond the `OwnerId` on temp shares.

## Success signal

An unauthenticated user visiting any protected app page (Upload, Account, or any future authenticated route) is redirected to `/login`. After authenticating via Google OAuth, they land on the upload page and can upload files, with each upload attributed to their user account. Anonymous upload API calls return 401 server-side. Existing download links (`/t/{ShareId}`) continue to work without auth. Monthly usage quotas are tracked per user and enforced at upload time against their plan limits. Copy on the Upload and Download pages no longer advertises "no login required."
