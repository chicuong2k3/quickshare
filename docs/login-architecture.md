# QuickShareCloud — Login Architecture

## Overview

QuickShareCloud uses a **single Google OAuth flow** on the Web frontend (port 9000). The Server API (port 59155) shares auth cookies via a **shared DataProtection keyring**.

> User → Web (9000) → Google OAuth → cookie → Server API (59155) decrypts cookie via shared keyring.

---

## 1. Component Map

| Layer | Project | File | Role |
|-------|---------|------|------|
| Login Page UI | `QuickShare.Web` | `Pages/Login.razor` | Renders OAuth provider buttons |
| OAuth Challenge | `QuickShare.Web` | `Program.cs` | `GET /login/{provider}` — triggers `ChallengeAsync()` |
| Web Cookie Auth | `QuickShare.Web` | `Program.cs` | `AddCookie()` — creates `.AspNetCore.QuickShare` cookie |
| Web DataProtection | `QuickShare.Web` | `Program.cs` | `PersistKeysToFileSystem` — shares keyring with Server |
| Cookie Forwarder | `QuickShare.Web` | `Infrastructure/CookieForwardingHandler.cs` | Forwards auth cookie from Web → Server API calls |
| Auth Constants | `QuickShare.Shared` | `AuthConstants.cs` | Cookie name, claim types (`ProviderClaimType`, `AvatarUrlClaimType`), provider identifiers (`GoogleProvider`, `DevProvider`) |
| Server Cookie Auth | `QuickShare.Server` | `Extensions/ApiCookieAuthExtensions.cs` | `AddCookie()` — reads same cookie |
| Server DataProtection | `QuickShare.Server` | `Program.cs` | `PersistKeysToFileSystem` — shares keyring with Web |
| Server User DB helper | `QuickShare.Server` | `Extensions/ClaimsPrincipalExtensions.cs` | `GetOrCreateUserAsync()` — lookup/create User from claims |
| Server User Mapper | `QuickShare.Server` | `Features/Users/Shared/UserMapper.cs` | Mapperly source-gen: `User` → `ProfileResponse` |
| Dev Auto-Signin | `QuickShare.Server` | `Program.cs` | Auto-signs in as `DevProvider` for development |

---

## 2. OAuth Configuration

Only the Web project registers Google OAuth. The Server only validates cookies.

| App | Config Path | Purpose |
|-----|-------------|---------|
| **Web** (port 9000) | `Auth:Google:ClientId` / `Auth:Google:ClientSecret` | End-user login |
| **Server** (port 59155) | N/A | Uses dev auto-signin in Development |

---

## 3. OnCreatingTicket — Register User at Login

```csharp
options.Events.OnCreatingTicket = async ctx =>
{
    ctx.Identity?.AddClaim(new Claim("provider", "google"));

    var factory = ctx.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient();
    var apiBase = builder.Configuration["QuickShare:ApiBaseUrl"] ?? "http://localhost:59155";
    client.BaseAddress = new Uri(apiBase);

    var email = ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value;
    var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value;
    var subject = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var avatar = ctx.Principal?.FindFirst("avatar_url")?.Value;

    if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(subject))
    {
        await client.PostAsJsonAsync("/api/users/register", new
        {
            Provider = "google",
            ProviderKey = subject,
            Name = name ?? email,
            Email = email,
            AvatarUrl = avatar
        });
    }
};
```

- Adds `"provider": "google"` claim
- Calls `POST /api/users/register` (idempotent: creates User + UserLogin + UserUsage if new)
- User is created BEFORE the cookie is set — first API call is fast (user already exists in DB)
- Avatar URL from Google is saved in DB immediately

---

## 4. Full Login Flow (Web-Driven)

```
Browser                  Web (9000)                  Google OAuth              Server (59155)
   │                         │                           │                        │
   │  GET /login             │                           │                        │
   │────────────────────────▶│                           │                        │
   │◀────────────────────────│ (Login.razor HTML)        │                        │
   │                         │                           │                        │
   │  Click "Continue with   │                           │                        │
   │  Google"                │                           │                        │
   │  GET /login/google      │                           │                        │
   │────────────────────────▶│                           │                        │
   │                         │  ChallengeAsync("google")                           │
   │                         │  → HTTP 302 to Google    │                        │
   │◀────────────────────────│                           │                        │
   │                         │                           │                        │
   │  Redirect to Google     │                           │                        │
   │────────────────────────────────────────────────────▶│                        │
   │                         │                           │                        │
   │  (User signs in)        │                           │                        │
   │◀────────────────────────────────────────────────────│                        │
   │                         │                           │                        │
   │  Callback:              │                           │                        │
   │  GET /auth/google/      │                           │                        │
   │  callback?code=...      │                           │                        │
   │────────────────────────▶│                           │                        │
   │                         │── Exchange code for ─────▶│                        │
   │                         │   access_token + id_token│                        │
   │                         │◀─────────────────────────│                        │
   │                         │                           │                        │
   │  OnCreatingTicket:      │                           │                        │
   │  Add provider=google    │                           │                        │
   │                         │                           │                        │
   │  Cookie auth signs in:  │                           │                        │
   │  Encrypt claims into    │                           │                        │
   │  .AspNetCore.QuickShare │                           │                        │
   │◀─ Set-Cookie ───────────│                           │                        │
   │◀─ Redirect to returnUrl │                           │                        │
   │                         │                           │                        │
   │  Now browser has cookie │                           │                        │
   │                         │                           │                        │
   │  GET /files (cookie)    │                           │                        │
   │────────────────────────▶│                           │                        │
   │                         │  ApiClient → Server       │                        │
   │                         │  Cookie forwarded via     │                        │
   │                         │  CookieForwardingHandler  │                        │
   │                         │                           │                        │
   │                         │  GET /api/files ─────────▶│                        │
   │                         │                           │  Decrypts cookie via   │
   │                         │                           │  shared keyring        │
   │                         │                           │                        │
   │                         │                           │  GetOrCreateUserAsync()│
   │                         │                           │  syncs user to DB      │
   │                         │◀──────────────────────────│                        │
   │◀────────────────────────│                           │                        │
```

---

## 5. Cookie Sharing via Shared DataProtection Keyring

### 5.1 The Problem: Two Processes, One Cookie

QuickShare Web and Server are **two separate processes**. When a user logs in via Web:

1. Web creates a `ClaimsPrincipal` with the user's identity
2. The cookie middleware **encrypts** it into the `.AspNetCore.QuickShare` cookie
3. Web calls the Server API — forwarding that cookie
4. Server needs to **decrypt** the cookie to know who the user is

Without a shared keyring, Server cannot decrypt Web's cookie.

### 5.2 Three Things That Must Match

| Requirement | Why? |
|-----------|------|
| **Same keyring** | Different master keys → cannot decrypt |
| **Same `ApplicationName("QuickShare")`** | Different name → different sub-key |
| **Same cookie name** (`.AspNetCore.QuickShare`) | Middleware only reads configured name |

### 5.3 Code (identical in both projects)

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("QuickShare");
```

---

## 6. CookieForwardingHandler

```
Browser request (has cookie)
  → Web: UseAuthentication sets User
  → Blazor page renders → ApiClient calls Server
  → CookieForwardingHandler copies Cookie header to outgoing request
  → Server receives cookie → decrypts via shared keyring
```

---

## 7. Auth Enforcement

### Web
- `AuthorizeRouteView` + `[Authorize]` attribute on `/files` and `/account`
- `NotAuthorized` template redirects to `/login?returnUrl=...`
- Public pages (/, /login, /pricing, /d/{code}, /p2p/*)

### Server
- FastEndpoints with cookie auth
- 401 → HTTP 401 (not redirect)

---

## 8. Dev Auto-Signin (Server)

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

## 9. Auth Configuration Comparison

| Aspect | Web (port 9000) | Server (port 59155) |
|--------|-----------------|----------------------|
| Cookie Name / Claim Types | `.AspNetCore.QuickShare` (via `AuthConstants.CookieName`) / `AuthConstants.ProviderClaimType`, `AuthConstants.AvatarUrlClaimType` | `.AspNetCore.QuickShare` (via `AuthConstants.CookieName`) |
| Login Path | `/login` | N/A |
| Logout Path | `/logout` | N/A |
| Cookie Expiry | 30 days, sliding | Default (session) |
| OAuth Providers | Google | N/A |
| Google Config | `Auth:Google:*` | N/A |
| Dev Auto-signin | No | Yes |
| Auth Redirect | Redirect to `/login` | Return 401 |

---

## 10. File Reference

| File | Role |
|------|------|
| `src/QuickShare.Web/Pages/Login.razor` | Login page UI |
| `src/QuickShare.Web/Program.cs` | Web auth: cookie + Google OAuth + `/login/{provider}` + `/logout` |
| `src/QuickShare.Web/Infrastructure/CookieForwardingHandler.cs` | Forwards auth cookie Web → Server |
| `src/QuickShare.Web/ApiClient.cs` | Named `HttpClient` for Server API calls |
| `src/QuickShare.Shared/AuthConstants.cs` | Shared cookie name, claim types (`ProviderClaimType`, `AvatarUrlClaimType`), provider identifiers (`GoogleProvider`, `DevProvider`) |
| `src/QuickShare.Server/Program.cs` | Server auth: cookie + dev auto-signin |
| `src/QuickShare.Server/Extensions/ApiCookieAuthExtensions.cs` | API cookie auth (401/403) |
| `src/QuickShare.Server/Extensions/ClaimsPrincipalExtensions.cs` | `GetUserId()` + `GetOrCreateUserAsync()` |
| `src/QuickShare.Server/Features/Users/Register/RegisterUserEndpoint.cs` | `POST /api/users/register` — idempotent user creation |
| `src/QuickShare.Server/Features/Users/Shared/UserMapper.cs` | Mapperly: `User` → `UserProfileResponse` |
| `src/QuickShare.Server/Data/AuditableEntityInterceptor.cs` | Auto-sets `CreatedAt`/`UpdatedAt` on save |
