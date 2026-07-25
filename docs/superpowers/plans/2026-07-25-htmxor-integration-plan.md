# Phase 3: Htmxor Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace client-side XHR/fetch calls with declarative HTMX attributes in Account.razor and Files.razor

**Architecture:** Blazor Static SSR pages with Htmxor. hx-boost already active on `<body>`. Task 1 converts the Account profile form to `hx-post` with an HTML-returning endpoint. Task 2 adds search debounce to Files.razor.

**Tech Stack:** Blazor Static SSR, Htmxor, HTMX, Alpine.js (Files.razor only)

**Global Constraints:**
- Do not change Alpine.js folder CRUD modals in Files.razor
- Do not change Download.razor JS state machine
- Do not change Pricing.razor billing toggle
- Account form must support file upload (avatar) via `hx-encoding="multipart/form-data"`
- Keep branding manager JS in Account.razor untouched

---

### Task 1: Convert Account profile form to hx-post

**Files:**
- Modify: `src/QuickShare.Web/Pages/Account.razor:45-68` (form wrapper + button)
- Modify: `src/QuickShare.Web/Pages/Account.razor:112-183` (remove submitAccountForm JS)
- Modify: `src/QuickShare.Web/Program.cs:110-166` (return HTML instead of JSON redirect)
- Test: `dotnet build` (no test suite for this page)

**Interfaces:**
- Consumes: Existing `/api/users/me/profile` PUT endpoint (unchanged)
- Produces: `/account/update` POST endpoint returns HTML string (full `#account-app` div) instead of JSON redirect

- [ ] **Step 1: Wrap profile inputs in `<form hx-post>`**

In `Account.razor`, replace the standalone button and inputs with a proper `<form>` element:

Old:
```razor
<div class="mt-10 space-y-5">
    <label class="db-field">
        <span class="db-label">Display name</span>
        <input id="name" name="name" type="text" required
               value="@name"
               class="db-input" />
    </label>

    <div>
        <label class="db-label db-block mb-2">Avatar image</label>
        <label for="avatarFile"
               class="flex cursor-pointer flex-col items-center gap-2 rounded-2xl border-2 border-dashed border-border/60 bg-background px-5 py-8 text-sm text-muted-foreground/80 hover:border-primary/40 hover:bg-primary/5 transition-colors">
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
            <span>Click to choose an image</span>
            <span class="text-xs text-muted-foreground/60">PNG, JPG or GIF — max 5 MB</span>
        </label>
        <input id="avatarFile" name="avatar" type="file" accept="image/*"
               class="sr-only" onchange="previewAvatar(event)" />
    </div>

    <button type="button" onclick="submitAccountForm()"
            class="db-btn db-btn--primary w-full">
        Save changes
    </button>
</div>
```

New:
```razor
<form hx-post="/account/update"
      hx-target="#account-app"
      hx-swap="outerHTML"
      hx-encoding="multipart/form-data">
    <div class="mt-10 space-y-5">
        <label class="db-field">
            <span class="db-label">Display name</span>
            <input id="name" name="name" type="text" required
                   value="@name"
                   class="db-input" />
        </label>

        <div>
            <label class="db-label db-block mb-2">Avatar image</label>
            <label for="avatarFile"
                   class="flex cursor-pointer flex-col items-center gap-2 rounded-2xl border-2 border-dashed border-border/60 bg-background px-5 py-8 text-sm text-muted-foreground/80 hover:border-primary/40 hover:bg-primary/5 transition-colors">
                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
                <span>Click to choose an image</span>
                <span class="text-xs text-muted-foreground/60">PNG, JPG or GIF — max 5 MB</span>
            </label>
            <input id="avatarFile" name="avatar" type="file" accept="image/*"
                   class="sr-only" onchange="previewAvatar(event)" />
        </div>

        <button type="submit"
                class="db-btn db-btn--primary w-full">
            Save changes
        </button>
    </div>
</form>
```

- [ ] **Step 2: Change /account/update endpoint to return HX-Redirect**

In `Program.cs`, replace the JSON redirect with an HTMX `HX-Redirect` response header. This tells HTMX to navigate to the account page (via AJAX) after the form submission succeeds. The page renders fresh from SSR with the updated profile.

Old:
```csharp
    return Results.Json(new { redirect = "/account" });
}).WithMetadata(SkipAntiforgeryValidation.Instance);
```

New:
```csharp
    httpContext.Response.Headers.Append("HX-Redirect", "/account");
    return Results.Ok();
}).WithMetadata(SkipAntiforgeryValidation.Instance);
```

For error cases, keep returning error responses that HTMX can handle:
```csharp
    // On validation/session error:
    httpContext.Response.Headers.Append("HX-Retarget", "#form-error");
    return Results.Content("<div class=\"db-alert db-alert--error\" role=\"alert\">Session expired. Please refresh the page.</div>", "text/html");
```

- [ ] **Step 3: Remove submitAccountForm JS from Account.razor**

Delete lines 112-183 (the `submitAccountForm()` function) from Account.razor. Keep the `previewAvatar()` function (still used for the avatar file input preview) and the branding manager JS.

- [ ] **Step 4: Remove `IAntiforgery` injection from Account.razor @code block**

Since HTMX handles antiforgery automatically (via `app.UseHtmxAntiforgery()` middleware in Program.cs), remove the antiforgery-related code:

Remove these lines from the @code block:
```csharp
// Remove entire lines:
private string? antiforgeryToken;

// And from OnInitializedAsync, remove:
var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();
antiforgeryToken = antiforgery.GetAndStoreTokens(httpContext).RequestToken;
```

Also remove the `@using Microsoft.AspNetCore.Antiforgery` import from the top of Account.razor since it's no longer needed.

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/QuickShare.Web`
Expected: 0 errors, 0 warnings


### Task 2: Add debounced search to Files.razor

**Files:**
- Modify: `src/QuickShare.Web/Pages/Files.razor:34-63` (search form HTMX attributes)
- Modify: `src/QuickShare.Web/Pages/Files.razor:192-338` (wrap file list in target div)

**Interfaces:**
- Consumes: Existing search form that submits to `/files?search=...` via GET
- Produces: File list wrapped in `<div id="files-list">` for hx-target

- [ ] **Step 1: Add hx-trigger and hx-target to search form**

In `Files.razor` line 35, add HTMX attributes to the existing form:

Old:
```razor
<form method="get" action="/files" class="relative">
```

New:
```razor
<form method="get" action="/files"
      hx-trigger="input changed delay:500ms"
      hx-target="#files-list"
      class="relative">
```

- [ ] **Step 2: Wrap file list section in target div**

In `Files.razor`, wrap the file list section (from the `<div class="mt-8">` that contains the Loading/Error/Empty/File list states) in `<div id="files-list">`. This includes the `@* ===== FILES SECTION ===== *@` comment block through the file count footer.

Old:
```razor
        @* ===== FILES SECTION ===== *@
        <div class="mt-8">
            @if (RenderState == FilesRenderState.Loading)
            {
```

New:
```razor
        @* ===== FILES SECTION ===== *@
        <div id="files-list" class="mt-8">
            @if (RenderState == FilesRenderState.Loading)
            {
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/QuickShare.Web`
Expected: 0 errors, 0 warnings