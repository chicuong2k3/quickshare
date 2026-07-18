---
stepsCompleted: [1, 2, 3]
inputDocuments:
  - _bmad-output/specs/spec-enforce-login/SPEC.md
  - _bmad-output/architecture/architecture-TeleCloud-2026-07-18/ARCHITECTURE-SPINE.md
---

# TeleCloud — Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for TeleCloud, decomposing the requirements from the Spec (SPEC-enforce-login) and Architecture Spine into implementable stories. The work is organized into three epics: (1) Auth Gate — enforce login on protected pages and endpoints, (2) Upload Attribution — link each temp share to its owner, and (3) Rate Limits — per-user plan-based upload quotas.

## Requirements Inventory

### Functional Requirements

| ID | Description | Source |
|---|---|---|
| FR-1 | Unauthenticated visitors to protected pages redirect to `/login?returnUrl=...` | CAP-1 |
| FR-2 | Upload page and create-temp-share API require authentication | CAP-2 |
| FR-3 | Download links (`/t/{ShareId}`) and their API endpoints remain public | CAP-3 |
| FR-4 | Each temp share record stores the uploading user's ID (`OwnerId`) | CAP-4 |
| FR-5 | FastEndpoints API enforces auth via `RequireAuthorization()` / `[Authorize]`, not UI hiding alone | CAP-5 |
| FR-6 | Monthly upload volume (GB) and transfer count tracked per user, enforced at upload time against plan tier | CAP-6 |
| FR-7 | Marketing pages (`/pricing`, etc.) remain accessible without authentication | CAP-7 |

### Non-Functional Requirements

| ID | Description |
|---|---|
| NFR-1 | Reuse existing `.AspNetCore.TeleCloud` cookie auth + Google OAuth middleware — do not replace the auth stack |
| NFR-2 | Download links must remain public — recipients never need accounts |
| NFR-3 | Reuse existing PostgreSQL `Users` / `UserLogins` tables — no new data stores or infrastructure |
| NFR-4 | Rate limit tiers: Free = 3 GB/month, 10 transfers; Starter = 300 GB/month, 10 transfers; Ultimate = unlimited |
| NFR-5 | Update copy on Upload.razor (remove "anonymous · no login required") and Download.razor (remove "No login required") |
| NFR-6 | Legacy temp shares with `OwnerId = null` are left as-is — rate limits don't apply retroactively |

### Additional Requirements

| ID | Description | Source |
|---|---|---|
| AR-1 | Add auth middleware to TeleCloud.Web `Program.cs` with public allowlist: `/login`, `/pricing`, `/t/{id}`, `/antiforgery`, `/logout`, static assets | AD-2 |
| AR-2 | Remove `AllowAnonymous()` from `CreateTempShareEndpoint` and `DownloadFileEndpoint` | AD-1 |
| AR-3 | Add `Guid? OwnerId` to `TempShare` entity + EF migration | AD-3 |
| AR-4 | Inject `CurrentUserService` in `CreateTempShareHandler` and populate `OwnerId` | AD-3 |
| AR-5 | Add `string Plan` (default `"free"`) to `User` entity + EF migration | AD-5 |
| AR-6 | Create `UserUsage` entity (`UserId`, `PeriodStart`, `TotalBytesUploaded`, `TransferCount`) + EF migration + `DbSet` | AD-4 |
| AR-7 | Create static `PlanTierLimits` class with `GetLimits(plan)` returning `(long? maxBytes, int? maxTransfers)` — null = unlimited | AD-4 |
| AR-8 | Wire rate limit check in `CreateTempShareHandler`: lookup/upsert `UserUsage`, check limits against file size + transfer count, return 403/429 if exceeded, atomically increment on success | AD-4 |

### UX Design Requirements

None — this feature does not introduce new UI surfaces. The only UX change is copy edits on existing pages (FR-5 / NFR-5).

### FR Coverage Map

| Story | FR-1 | FR-2 | FR-3 | FR-4 | FR-5 | FR-6 | FR-7 |
|---|---|---|---|---|---|---|---|
| 1.1 — Auth Web Middleware | ✓ | ✓ | ✓ | | | | ✓ |
| 1.2 — Endpoint Hardening | | ✓ | | | ✓ | | |
| 1.3 — Copy Updates | | ✓ | | | | | |
| 2.1 — TempShare.OwnerId | | | | ✓ | | | |
| 2.2 — Wire Attribution | | | | ✓ | | | |
| 3.1 — User.Plan Field | | | | | | ✓ | |
| 3.2 — UserUsage Entity | | | | | | ✓ | |
| 3.3 — PlanTierLimits | | | | | | ✓ | |
| 3.4 — Enforce Rate Limits | | | | | | ✓ | |

## Epic List

| Epic | Title | Stories |
|---|---|---|
| 1 | Auth Gate — Enforce Login on Protected Pages | 3 |
| 2 | Upload Attribution — Link Temp Shares to Their Owner | 2 |
| 3 | Plan Tiers & Rate Limits | 4 |

---

## Epic 1: Auth Gate — Enforce Login on Protected Pages

**Goal:** Any page or route that is not explicitly public redirects unauthenticated visitors to `/login`. Upload and API endpoints require authentication. Download links remain public. Copy no longer advertises "no login required."

This epic binds CAP-1, CAP-2, CAP-5, CAP-7 via AD-1, AD-2, AD-6, AD-7.

### Story 1.1: Add Auth Middleware to TeleCloud.Web

As a **visitor**,
I want **to be redirected to `/login` when I visit a page that requires authentication**,
So that **I cannot access protected features without logging in**.

**Acceptance Criteria:**

**Given** I am not authenticated
**When** I visit `/` (Upload page)
**Then** I am redirected to `/login?returnUrl=/`

**Given** I am not authenticated
**When** I visit `/account`
**Then** I receive a 302 redirect to `/login?returnUrl=/account`

**Given** I am not authenticated
**When** I visit `/pricing`
**Then** the page renders normally — I am not redirected

**Given** I am not authenticated
**When** I visit `/t/{someShareId}`
**Then** the page renders normally — I am not redirected

**Given** I am not authenticated
**When** I visit `/login`
**Then** the page renders normally

**Technical Notes:**
- Add middleware in `TeleCloud.Web/Program.cs` before `UseAntiforgery` / `MapRazorComponents`
- Allowlist: `/login`, `/pricing`, `/t/`, `/antiforgery`, `/logout`, `/css/`, `/js/`, `/lib/`
- Use path-based matching (prefix match for `/t/`, `/css/`, etc.)
- Redirect to `/login?returnUrl={originalPath}`
- Existing cookie auth config (`LoginPath = "/login"`) is already correct — no changes needed per AD-7

### Story 1.2: Harden FastEndpoints Endpoints

As an **API consumer**,
I want **endpoints that create shares or download files to reject unauthenticated requests**,
So that **anonymous users cannot upload or access protected resources**.

**Acceptance Criteria:**

**Given** I send a POST to `/api/temp` without a valid auth cookie
**When** the request reaches the server
**Then** I receive a 401 response

**Given** I send a GET to `/api/files/{id}/download` without a valid auth cookie
**When** the request reaches the server
**Then** I receive a 401 response

**Given** I send a GET to `/api/temp/{id}` (metadata) without a valid auth cookie
**When** the request reaches the server
**Then** I receive a 200 response with share metadata (public)

**Given** I send a GET to `/api/temp/{id}/download` (file download) without a valid auth cookie
**When** the request reaches the server
**Then** I receive a 200 response with the file (public)

**Technical Notes:**
- Remove `AllowAnonymous()` from `CreateTempShareEndpoint.cs`
- Remove `AllowAnonymous()` from `DownloadFileEndpoint.cs`
- Keep `AllowAnonymous()` on `GetTempShareEndpoint.cs` and `DownloadTempShareEndpoint.cs`
- Confirm API cookie auth config already returns 401 (not HTML redirect) per AD-7 — verify `ApiCookieAuthExtensions.cs` is unchanged

### Story 1.3: Update Copy on Upload and Download Pages

As a **visitor**,
I want **the upload and download pages to reflect that login is now required**,
So that **I'm not misled by outdated messaging about anonymous usage**.

**Acceptance Criteria:**

**Given** I view the Upload page while authenticated
**Then** the page does not display "anonymous · no login required"

**Given** I view the Download page
**Then** the page does not display "No login required"

**Technical Notes:**
- Edit `Upload.razor`: remove or replace the "anonymous · no login required" text
- Edit `Download.razor`: remove or replace the "No login required" text
- Simple copy changes only — no structural changes to the pages

---

## Epic 2: Upload Attribution — Link Temp Shares to Their Owner

**Goal:** Each temp share record stores the authenticated user's ID at creation time, enabling accountability and per-user rate limiting.

This epic binds CAP-4 via AD-3.

### Story 2.1: Add OwnerId to TempShare Entity

As a **developer**,
I want **the TempShare entity to have an `OwnerId` column**,
So that **each share can be attributed to the user who uploaded it**.

**Acceptance Criteria:**

**Given** the TempShare entity has been updated
**When** inspecting the `TempShare` class
**Then** it has a `Guid? OwnerId` property

**Given** EF migration has been applied
**When** inspecting the database
**Then** the `temp_shares` table has a nullable `owner_id` column with a foreign key to `users`

**Technical Notes:**
- Add `public Guid? OwnerId { get; set; }` to `TempShare.cs`
- Add `public User? Owner { get; set; }` navigation property
- Add EF migration via `dotnet ef migrations add AddTempShareOwnerId`
- Foreign key to `users` table
- Nullable — existing rows retain null (AD-8)

### Story 2.2: Wire CurrentUserService in CreateTempShareHandler

As a **developer**,
I want **the create-temp-share handler to set `OwnerId` from the current user**,
So that **new shares are attributed correctly**.

**Acceptance Criteria:**

**Given** an authenticated user uploads a file
**When** the `CreateTempShareHandler` executes
**Then** the created `TempShare` record has `OwnerId` set to the user's GUID

**Given** the handler injects `CurrentUserService`
**When** `GetUserIdAsync()` is called
**Then** it returns a non-null GUID (handler fails with 401 if null — though the endpoint auth gate should prevent this)

**Technical Notes:**
- Inject `CurrentUserService` into `CreateTempShareHandler`
- Call `await _currentUserService.GetUserIdAsync()` — this already exists and resolves the user
- Set `ownerId = userId` on the new TempShare before persisting

---

## Epic 3: Plan Tiers & Rate Limits

**Goal:** Monthly upload volume and transfer count are tracked per user and enforced at upload time against their plan tier's limits before a share is created.

This epic binds CAP-6 via AD-4, AD-5.

### Story 3.1: Add Plan Field to User Entity

As a **developer**,
I want **the User entity to have a `Plan` field defaulting to `"free"`**,
So that **each user is assigned a plan tier that determines their usage limits**.

**Acceptance Criteria:**

**Given** the User entity has been updated
**When** inspecting the `User` class
**Then** it has a `string Plan` property with default `"free"`

**Given** a new user is created via `CurrentUserService.GetOrCreateUserAsync()`
**When** the user row is inserted
**Then** the `plan` column is set to `"free"`

**Technical Notes:**
- Add `public string Plan { get; set; } = PlanTierLimits.Free;` to `User.cs` (use the constant from Story 3.3, not a magic string)
- Add EF migration via `dotnet ef migrations add AddUserPlan` — set default value to `"free"` for existing rows
- No enum — string column (`"free"`, `"starter"`, `"ultimate"`) for future flexibility
- The PlanTierLimits class (Story 3.3) defines `public const string` constants that are the single source of truth for valid values

### Story 3.2: Create UserUsage Entity

As a **developer**,
I want **a `UserUsage` entity that tracks monthly usage per user**,
So that **the system can record and query upload volume and transfer counts**.

**Acceptance Criteria:**

**Given** the UserUsage entity has been created
**When** inspecting the class
**Then** it has properties: `Id`, `UserId`, `PeriodStart`, `TotalBytesUploaded`, `TransferCount`

**Given** EF migration has been applied
**When** inspecting the database
**Then** a `user_usage` table exists with columns `id`, `user_id`, `period_start`, `total_bytes_uploaded`, `transfer_count` and a foreign key to `users`

**Given** `TeleCloudDbContext` has been updated
**When** inspecting the context
**Then** a `DbSet<UserUsage> UserUsages { get; set; }` is present

**Technical Notes:**
- Entity properties:
  - `Guid Id`
  - `Guid UserId`
  - `DateTime PeriodStart` — start of the current billing period (calendar month)
  - `long TotalBytesUploaded`
  - `int TransferCount`
- Navigation property: `User User`
- `TeleCloudDbContext`: add `public DbSet<UserUsage> UserUsages => Set<UserUsage>();`
- Configure in `OnModelCreating`: unique index on `(UserId, PeriodStart)`, foreign key to `users`
- Migration via `dotnet ef migrations add AddUserUsage`

### Story 3.3: Create PlanTierLimits Static Lookup

As a **developer**,
I want **a single static class that defines plan tier limits**,
So that **rate limit enforcement has a single source of truth for quota values**.

**Acceptance Criteria:**

**Given** I call `PlanTierLimits.GetLimits("free")`
**When** inspecting the result
**Then** it returns `(maxBytes: 3 * 1024^3, maxTransfers: 10)` — 3 GB, 10 transfers

**Given** I call `PlanTierLimits.GetLimits("starter")`
**When** inspecting the result
**Then** it returns `(maxBytes: 300 * 1024^3, maxTransfers: 10)` — 300 GB, 10 transfers

**Given** I call `PlanTierLimits.GetLimits("ultimate")`
**When** inspecting the result
**Then** it returns `(maxBytes: null, maxTransfers: null)` — unlimited

**Given** I call `PlanTierLimits.GetLimits("unknown-plan")`
**When** inspecting the result
**Then** it returns the free-tier limits (safe default)

**Technical Notes:**
- Create `PlanTierLimits` static class in `Features/TempShares/Shared/` or `Data/`
- Define plan name constants: `public const string Free = "free"`, `public const string Starter = "starter"`, `public const string Ultimate = "ultimate"`
- Return type: `(long? MaxBytesPerMonth, int? MaxTransfersPerMonth)` — null = unlimited
- Values: Free = 3 GB (3,221,225,472 bytes) / 10 transfers; Starter = 300 GB / 10 transfers; Ultimate = unlimited
- Case-insensitive lookup (use `string.Equals(plan, StringComparison.OrdinalIgnoreCase)`)
- Invalid plan names fall back to free-tier limits
- `User.cs` should reference `PlanTierLimits.Free` as the default, not the raw string `"free"`

### Story 3.4: Enforce Rate Limits in CreateTempShareHandler

As a **free-tier user**,
I want **my upload to be rejected if I've exceeded my monthly quota**,
So that **I cannot circumvent plan limits**.

**Acceptance Criteria:**

**Given** a free-tier user has uploaded 3 GB this month
**When** they try to upload another file
**Then** they receive a 403 response with message "Monthly upload limit reached. Upgrade your plan to upload more."

**Given** a free-tier user has made 10 transfers this month
**When** they try to upload another file
**Then** they receive a 403 response with message "Monthly transfer limit reached. Upgrade your plan to upload more."

**Given** an ultimate-tier user has uploaded 500 GB this month
**When** they try to upload another file
**Then** the upload succeeds (unlimited plan)

**Given** a starter-tier user has uploaded 200 GB (under 300 GB limit) this month
**When** they try to upload a file
**Then** the upload succeeds (under limit)

**Given** a user has no existing `UserUsage` row for the current period
**When** they upload a file
**Then** a new `UserUsage` row is created with their file size and transfer count = 1, and the upload succeeds

**Technical Notes:**
- In `CreateTempShareHandler`, before creating the share:
  1. Resolve user's plan from `dbContext.Users` (or `CurrentUserService`)
  2. Get plan limits from `PlanTierLimits.GetLimits(plan)`
  3. Lookup or upsert `UserUsage` row for the current calendar month
  4. If `TotalBytesUploaded + fileSize > MaxBytes` → return 403 with error message
  5. If `TransferCount + 1 > MaxTransfers` → return 403 with error message
  6. If both OK, increment usage row atomically within the same EF transaction as share creation
- **Race condition — concurrent first-upload-of-period**: Two uploads hitting at the same time for a user with no existing `UserUsage` row will both attempt to INSERT, and one gets a unique constraint violation on `(UserId, PeriodStart)`. Use PostgreSQL `INSERT ... ON CONFLICT DO UPDATE` via EF Core's `dbContext.UserUsages.Upsert(...)` (requires flexiding/FlexLabs.Upsert or raw SQL) to atomically create-or-increment without a retry loop.
  - If the upsert library isn't available, use a raw SQL approach:
    ```sql
    INSERT INTO user_usage (id, user_id, period_start, total_bytes_uploaded, transfer_count)
    VALUES (@id, @userId, @periodStart, @bytes, 1)
    ON CONFLICT (user_id, period_start) DO UPDATE
    SET total_bytes_uploaded = user_usage.total_bytes_uploaded + @bytes,
        transfer_count = user_usage.transfer_count + 1;
    ```
- Return `Results.Failure<CreateTempShareResult>(...)` with typed error response
- Error response body: `{ "error": "quota_exceeded", "message": "...", "limit_type": "bytes" | "transfers" }`
- Period start: find existing row where `PeriodStart <= now && PeriodStart + 1 month > now`. If none exists, create new row with `PeriodStart = first day of current month`.

### Deferred — Mid-Tier Upgrade Usage Carryover

**Context:** If a Free user upgrades to Ultimate mid-month, uploads 100 GB, then downgrades back to Starter next month, their accumulated `UserUsage` row still carries the 100 GB from the Ultimate period — which may exceed Starter's 300 GB limit even though only post-downgrade uploads should count.

**Resolution:** Deferred. Payment integration doesn't exist yet, so this edge case has no trigger. When billing lands, revisit the period logic to either (a) reset the usage row on plan change, or (b) tie the billing period to the subscription cycle rather than the calendar month. No changes needed now.
