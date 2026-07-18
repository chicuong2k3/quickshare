---
baseline_commit: a1ecb33283f306f05d7d7145e87fb54cf4800de7
---

# Story 1.1: Add Auth Middleware to TeleCloud.Web

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a **visitor**,
I want **to be redirected to `/login` when I visit a page that requires authentication**,
So that **I cannot access protected features without logging in**.

## Acceptance Criteria

1. **AC-1 (Upload redirect):** Given I am not authenticated, when I visit `/`, then I am redirected via 302 to `/login?returnUrl=/`.

2. **AC-2 (Account redirect):** Given I am not authenticated, when I visit `/account`, then I am redirected via 302 to `/login?returnUrl=/account`.

3. **AC-3 (Pricing public):** Given I am not authenticated, when I visit `/pricing`, then the page renders normally (200, no redirect).

4. **AC-4 (Download public):** Given I am not authenticated, when I visit `/t/{someShareId}`, then the page renders normally (200, no redirect).

5. **AC-5 (Login public):** Given I am not authenticated, when I visit `/login`, then the page renders normally.

6. **AC-6 (Logout public):** Given I am not authenticated, when I visit `/logout`, then the endpoint executes normally (no redirect).

7. **AC-7 (Antiforgery public):** Given I am not authenticated, when I visit `/antiforgery`, then the token endpoint returns normally.

8. **AC-8 (Unknown path redirect):** Given I am not authenticated, when I visit any path not in the allowlist, then I am redirected to `/login?returnUrl={originalPath}`.

9. **AC-9 (Static assets pass):** Given I am not authenticated, when the browser requests `/css/app.css`, then the file is served without redirect.

## Tasks / Subtasks

- [x] **Task 1: Create `AuthPageRedirectMiddleware` class** (AC: 1-9)
  - [x] Create `src/TeleCloud.Web/Infrastructure/AuthPageRedirectMiddleware.cs`
  - [x] Define public allowlist paths: `/login`, `/pricing`, `/t/`, `/antiforgery`, `/logout`
  - [x] Implement path-based prefix matching (not regex) — `/t/` allows any `/t/{anything}`
  - [x] If user is not authenticated AND path not in allowlist → 302 redirect to `/login?returnUrl={originalPath}`
  - [x] If user IS authenticated OR path IS in allowlist → call `await _next(context)`
  - [x] Use `context.Request.PathBase + context.Request.Path` for the returnUrl value (handles reverse proxy prefixes)

- [x] **Task 2: Register middleware in Program.cs** (AC: 1-9)
  - [x] Add `app.UseMiddleware<AuthPageRedirectMiddleware>()` between `app.UseAuthorization()` (line 91) and `app.UseAntiforgery()` (line 94)
  - [x] Do NOT modify `app.UseAuthentication()` / `app.UseAuthorization()` — they're already correct
  - [x] Do NOT touch `app.UseStaticFiles()` — it runs before auth by design

- [x] **Task 3: Verify with diagnostics** (AC: 1-9)
  - [x] Run `dotnet build` on `src/TeleCloud.Web/` — succeeded with zero errors
  - [x] Run `dotnet build` on whole solution — TeleCloud.Server fails due to pre-existing file lock (PID 3732), NOT caused by this change
  - [x] No LSP diagnostics errors (LSP server not installed — user declined)

## Dev Notes

### Middleware Implementation Pattern

```csharp
// src/TeleCloud.Web/Infrastructure/AuthPageRedirectMiddleware.cs
namespace TeleCloud.Web.Infrastructure;

public class AuthPageRedirectMiddleware
{
    private static readonly string[] PublicPathPrefixes =
    [
        "/login",
        "/pricing",
        "/t/",
        "/antiforgery",
        "/logout"
    ];

    private readonly RequestDelegate _next;

    public AuthPageRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Already authenticated → pass through
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        // Check allowlist: exact match or prefix match
        foreach (var prefix in PublicPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        // Not authenticated and not public → redirect to login
        var returnUrl = context.Request.PathBase + context.Request.Path;
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }
}
```

### Pipeline Position

```
app.UseStaticFiles();       // Line 87 — serves wwwroot/ before any auth
app.UseAuthentication();    // Line 90 — establishes User principal
app.UseAuthorization();     // Line 91 — checks [Authorize] on endpoints
app.UseMiddleware<AuthPageRedirectMiddleware>();  // ← INSERT HERE (page-level redirect)
app.UseAntiforgery();       // Line 94 — antiforgery token validation
```

**Why this order:**
- `UseStaticFiles()` must remain first — static assets never need auth
- `UseAuthentication()` must run before our middleware so `context.User` is populated
- `UseAuthorization()` runs before us, so API endpoints (FastEndpoints) are already protected independently
- Our middleware catches Blazor SSR page requests that fall through — if user is not authenticated and the path isn't public, we redirect
- `UseAntiforgery()` after us ensures antiforgery validation on POSTs still works

### Existing Code That Must Not Change

| File | Lines | Why untouchable |
|------|-------|----------------|
| `Program.cs` lines 32-51 | Cookie + Google OAuth config | LoginPath = "/login" already correct per AD-7 |
| `Program.cs` lines 90-91 | `UseAuthentication()` / `UseAuthorization()` | Correct position and order |
| `Program.cs` lines 117-126 | `MapRazorComponents`, `/antiforgery`, `MapStaticAssets` | App bootstrap and public endpoints |
| `Login.razor` lines 6-7 | `returnUrl` query param handling | Already reads returnUrl for post-login redirect |
| `Pages/*.razor` | Page `@page` directives | Must not change — routing is correct |
| `Routes.razor` | `<Router>` component | No changes needed |

### Allowlist Rationale

| Path | Reason |
|------|--------|
| `/login` | Login page + OAuth challenge endpoints (`/login/{provider}`) |
| `/pricing` | Marketing page — always public per FR-7 |
| `/t/` | Download page `/t/{ShareId}` — always public per FR-3 |
| `/antiforgery` | Token endpoint needed by unauthenticated POST scenarios |
| `/logout` | Must be accessible to sign out even if cookies are stale |
| `/css/`, `/js/`, `/lib/` | Static assets — served by UseStaticFiles() which runs before our middleware |

### Static Asset Note

`app.UseStaticFiles()` at line 87 runs before any auth middleware, so `/css/`, `/js/`, `/lib/` paths are handled before our middleware ever sees the request. The allowlist does NOT need to include static asset prefixes — they never reach our middleware. This is verified by the existing pipeline order.

### Files to Create

- `src/TeleCloud.Web/Infrastructure/AuthPageRedirectMiddleware.cs` (NEW)

### Files to Modify

- `src/TeleCloud.Web/Program.cs` — add `app.UseMiddleware<AuthPageRedirectMiddleware>()` between line 91 and line 94

### Testing Notes

Manual verification scenarios:
1. **Incognito browser → visit `/`** → should redirect to `/login?returnUrl=/`
2. **Incognito browser → visit `/account`** → should redirect to `/login?returnUrl=/account`
3. **Incognito browser → visit `/pricing`** → should render pricing page
4. **Incognito browser → visit `/t/some-guid`** → should render download page
5. **Incognito browser → visit `/login`** → should render login page
6. **After Google OAuth login → redirect to `/`** → should show upload page
7. **Authenticated browser → visit `/account`** → should show account page

### What This Story Does NOT Do

- Does not add auth to FastEndpoints API endpoints (Story 1.2)
- Does not update copy on Upload/Download pages (Story 1.3)
- Does not add OwnerId to TempShare (Epic 2)
- Does not add rate limits (Epic 3)
- Does not change any Razor page content
- Does not modify cookie auth or OAuth configuration

### References

- [Source: epics.md#Story-1.1] — Story definition, acceptance criteria, technical notes
- [Source: SPEC.md#CAP-1] — Capability intent: auth gate for protected pages
- [Source: SPEC.md#CAP-3] — Download links must remain public
- [Source: SPEC.md#CAP-7] — Public pages stay public
- [Source: src/TeleCloud.Web/Program.cs] — Current middleware pipeline (lines 84-128)

## Dev Agent Record

### Agent Model Used

deepseek-v4-flash-free (Sisyphus via bmad-dev-story)

### Debug Log References

### Completion Notes List

- ✅ Created `AuthPageRedirectMiddleware.cs` — 51 lines, allowlist of 5 public paths, 302 redirect for unauthenticated non-public requests
- ✅ Registered middleware in `Program.cs` at line 92, between `UseAuthorization()` and `UseAntiforgery()`
- ✅ `dotnet build src/TeleCloud.Web/TeleCloud.Web.csproj` — 0 warnings, 0 errors
- ✅ No test projects exist in repo — verification is build-based only
- ⚠️ Full solution `dotnet build` fails due to pre-existing file lock (`TeleCloud.Querying.dll` locked by PID 3732) — NOT caused by this change

### File List

- `src/TeleCloud.Web/Infrastructure/AuthPageRedirectMiddleware.cs` (NEW)
- `src/TeleCloud.Web/Program.cs` (MODIFIED — added `app.UseMiddleware<AuthPageRedirectMiddleware>()` at line 92)
- `_bmad-output/implementation-artifacts/1-1-add-auth-middleware-to-telecloud-web.md` (MODIFIED — task checkboxes, status, Dev Agent Record)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (MODIFIED — story status updated)
