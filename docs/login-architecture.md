# QuickShare — Login Architecture

## Overview

QuickShare uses a **single Google OAuth flow** in the monolith app (port 9000). Since the API and Blazor SSR run in the same process, there is no cross-process cookie forwarding — the cookie is set and validated within the same app.

> User → QuickShare (9000) → Google OAuth → cookie → all subsequent page/API requests use same cookie (single process).

---

## 1. Component Map

| Layer | File | Role |
|-------|------|------|
| Login Page UI | `Pages/Login.razor` | Renders OAuth provider buttons |
| OAuth Challenge | `Program.cs` | `GET /login/{provider}` — triggers `ChallengeAsync()` |
| Cookie Auth | `Program.cs` | Single `AddCookie()` — dual behavior: `/api/*` → 401, else redirect `/login` |
| DataProtection | `Program.cs` | `PersistKeysToFileSystem` + `SetApplicationName("QuickShare")` — single app, no cross-process sharing needed |
| User Registration Service | `Features/Users/Shared/UserRegistrationService.cs` | `RegisterAsync()` — idempotent user creation, called from OAuth `OnCreatingTicket` (all providers) |
| Auth Constants | `Infrastructure/AuthConstants.cs` | Cookie name, claim types, provider identifiers |
| ClaimsPrincipal Extensions | `Extensions/ClaimsPrincipalExtensions.cs` | `GetUserAsync()` — lookup User from claims |
| User Mapper | `Features/Users/Shared/UserMapper.cs` | Mapperly: `User` → `ProfileResponse` |
| Dev Auto-Signin | `Program.cs` | Auto-signs in as `DevProvider` for development |

---

## 2. OAuth Configuration

Single `AddAuthentication().AddCookie().AddGoogle()` registration in `Program.cs`.

| Config Path | Purpose |
|-------------|---------|
| `Authentication:Google:ClientId` / `Authentication:Google:ClientSecret` | Google OAuth credentials |

---

## 3. OnCreatingTicket — Register User at Login (In-Process)

```csharp
options.Events.OnCreatingTicket = async ctx =>
{
    ctx.Identity?.AddClaim(new Claim(AuthConstants.ProviderClaimType, AuthConstants.GoogleProvider));

    var email = ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value;
    var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value;
    var subject = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var avatar = ctx.User.TryGetProperty("picture", out var pic) ? pic.GetString() : null;

    if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(subject))
    {
        // Register in-process (no HTTP roundtrip) via the shared registration service.
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        await UserRegistrationService.RegisterAsync(
            AuthConstants.GoogleProvider,
            subject,
            name ?? email,
            email,
            avatar,
            db,
            ctx.HttpContext.RequestAborted);
    }
};
```

- Adds `"provider": "google"` claim — the **only** claim added. Avatar/plan are read from the DB via `GetUserAsync()` per request, not from claims.
- Calls `UserRegistrationService.RegisterAsync()` **in-process** (no HTTP roundtrip to a separate Server)
- User is created BEFORE the cookie is set

---

## 4. Login Flow

```
Browser                  QuickShare (9000)           Google OAuth
   │                         │                           │
   │  GET /login             │                           │
   │────────────────────────▶│                           │
   │◀────────────────────────│ (Login.razor HTML)        │
   │                         │                           │
   │  Click "Continue with   │                           │
   │  Google"                │                           │
   │  GET /login/google      │                           │
   │────────────────────────▶│                           │
   │                         │  ChallengeAsync("google") │
   │◀────────────────────────│  → HTTP 302 to Google    │
   │                         │                           │
   │  Redirect to Google     │                           │
   │────────────────────────────────────────────────────▶│
   │◀────────────────────────────────────────────────────│
   │                         │                           │
   │  Callback:              │                           │
   │  GET /signin-google?    │                           │
   │  code=...               │                           │
   │────────────────────────▶│                           │
   │                         │── Exchange code ─────────▶│
   │                         │◀─ access_token ───────────│
   │                         │                           │
   │  OnCreatingTicket:      │                           │
   │  Add provider=google    │                           │
   │  UserRegistrationService│                           │
   │  .RegisterAsync()       │                           │
   │  (in-process, direct DB)│                           │
   │                         │                           │
   │  Set cookie:            │                           │
   │  .AspNetCore.QuickShare │                           │
   │◀─ Set-Cookie ───────────│                           │
   │◀─ Redirect to returnUrl │                           │
   │                         │                           │
   │  GET /files (cookie)    │                           │
   │────────────────────────▶│                           │
   │  [Authorize] passes     │                           │
│  Page calls IMediator    │                           │
│  / AppDbContext in-proc │                           │
   │◀────────────────────────│                           │
```

---

## 5. Auth Enforcement

### Dual Cookie Behavior (single `AddCookie`)

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = AuthConstants.CookieName;
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    })
    .AddGoogle(options => { /* ... */ });
```

- **Page requests** (`/files`, `/account`): unauthenticated → redirect `/login`
- **API requests** (`/api/*`): unauthenticated → 401 JSON, unauthorized → 403 JSON
- FastEndpoints secure-by-default: endpoints without `AllowAnonymous()` require auth

### Protected Pages
- `AuthorizeRouteView` + `[Authorize]` attribute on `/files`, `/account`, `/account/profile`, `/account/billing`, `/upgrade`
- Public pages: `/`, `/login`, `/pricing`, `/d/{code}`, `/p2p/*`

---

## 6. Dev Auto-Signin

In Development, auto-authenticates unauthenticated requests as "dev-user":

```csharp
if (app.Environment.IsDevelopment())
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "dev-user"),
                new Claim(AuthConstants.ProviderClaimType, AuthConstants.DevProvider),
                new Claim(ClaimTypes.Name, "Dev User"),
                new Claim(ClaimTypes.Email, "dev@localhost")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.User = new ClaimsPrincipal(identity);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ctx.User);
        }
        await next();
    });
}
```

---

## 7. File Reference

| File | Role |
|------|------|
| `src/QuickShare/Pages/Login.razor` | Login page UI |
| `src/QuickShare/Program.cs` | Single auth registration: cookie + Google OAuth + dev auto-signin |
| `src/QuickShare/Features/Users/Shared/UserRegistrationService.cs` | DRY user registration (all OAuth providers) |
| `src/QuickShare/Features/Users/Shared/UserMapper.cs` | Mapperly: `User` → `ProfileResponse` |
| `src/QuickShare/Extensions/ClaimsPrincipalExtensions.cs` | `GetUserAsync()` — User lookup from claims |
| `src/QuickShare/Infrastructure/AuthConstants.cs` | Shared cookie name, claim types, provider identifiers |
