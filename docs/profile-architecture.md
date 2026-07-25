# QuickShareCloud — Profile Architecture

## Overview

The profile system displays and updates user identity (name, avatar) fetched from the Server API. The Server lazily creates users in the DB on first API access via `GetOrCreateUserAsync()`.

---

## 1. Component Map

| Layer | Project | File | Role |
|-------|---------|------|------|
| Account Page (UI) | `QuickShare.Web` | `Pages/Account.razor` | Profile card + delivery branding manager |
| Account JS | `QuickShare.Web` | `Pages/Account.razor.js` | Avatar preview + branding CRUD |
| Account CSS | `QuickShare.Web` | `Pages/Account.razor.css` | Scoped styles |
| Profile Form Handler | `QuickShare.Web` | `Pages/Account.razor` (code-behind) | `HandleUpdateProfile()` — validates + submits |
| Web Cookie Re-sign | `QuickShare.Web` | `Pages/Account.razor` (line 146) | `ctx.SignInAsync()` after profile update |
| Server Register Endpoint | `QuickShare.Server` | `Features/Users/Register/RegisterUserEndpoint.cs` | `POST /api/users/register` — idempotent user creation at login |
| Server Profile Endpoint | `QuickShare.Server` | `Features/Users/Profile/GetUserProfileEndpoint.cs` | `GET /api/users/me/profile` |
| Server Update Endpoint | `QuickShare.Server` | `Features/Users/Profile/UpdateUserProfileEndpoint.cs` | `PUT /api/users/me/profile` |
| Server User Mapper | `QuickShare.Server` | `Features/Users/Shared/UserMapper.cs` | Mapperly source-gen: `User` → `ProfileResponse` |
| Server User DB Helper | `QuickShare.Server` | `Extensions/ClaimsPrincipalExtensions.cs` | `GetOrCreateUserAsync()` — lazy user creation |
| Shared Models | `QuickShare.Shared` | `Models/ProfileResponse.cs` | `ProfileResponse` DTO |

---

## 2. Data Flow — View Profile

```
Browser                  Web (9000)                  Server (59155)              PostgreSQL
   │                         │                           │                          │
   │  GET /account (cookie)  │                           │                          │
   │────────────────────────▶│                           │                          │
   │                         │                           │                          │
   │  OnInitializedAsync()   │                           │                          │
   │                         │── GET /api/users/me/ ────▶│                          │
   │                         │   profile                 │                          │
   │                         │                           │                          │
   │                         │  Cookie forwarded via     │                          │
   │                         │  CookieForwardingHandler  │                          │
   │                         │                           │                          │
   │                         │                           │── Decrypt cookie ──────▶│
   │                         │                           │◀── ClaimsPrincipal ─────│
   │                         │                           │                          │
   │                         │                           │── GetOrCreateUserAsync()─▶│
   │                         │                           │   (lazy DB sync)        │
   │                         │                           │◀── User row ────────────│
   │                         │                           │                          │
   │                         │◀── ProfileResponse ───────│                          │
   │                         │    { Name, Email,         │                          │
   │                         │      AvatarUrl, Plan }    │                          │
   │                         │                           │                          │
   │◀─ Account.razor HTML ───│                           │                          │
   │   + profile data        │                           │                          │
```

### GetOrCreateUserAsync() — Lazy User Sync

Since the Web's Google OAuth does NOT create users in DB at login time, the Server lazily creates users on first API access:

```csharp
// ClaimsPrincipalExtensions.cs
public static async Task<User> GetOrCreateUserAsync(this ClaimsPrincipal principal, AppDbContext db)
{
    var nameId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var email = principal.FindFirst(ClaimTypes.Email)?.Value;
    var name = principal.FindFirst(ClaimTypes.Name)?.Value;
    var provider = principal.FindFirst("provider")?.Value ?? "unknown";

    var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (existing != null) return existing;

    var user = new User
    {
        Id = Guid.CreateVersion7(),
        Name = name ?? email ?? "unknown",
        Email = email ?? "",
        AvatarUrl = principal.FindFirst("avatar_url")?.Value ?? "",
        Plan = PlanTierLimits.Free
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return user;
}
```

This means:
- First visit to `/account` or `/files` triggers user creation with Plan=Free
- The `local_user_id` claim is NOT in the auth cookie at this point (cookies are immutable after sign-in)
- Server uses `email` to find existing user across sessions

---

## 3. Data Flow — Update Profile

```
Browser                  Web (9000)                  Server (59155)              Disk
   │                         │                           │                          │
   │  POST /account          │                           │                          │
   │  (hx-post, hx-target=   │                           │                          │
   │   "#profile-card")      │                           │                          │
   │  multipart/form-data    │                           │                          │
   │────────────────────────▶│                           │                          │
   │                         │                           │                          │
   │  HandleUpdateProfile()  │                           │                          │
   │  ├─ Validate name       │                           │                          │
   │  ├─ Validate avatar     │                           │                          │
   │  │  (max 5MB, image/*)  │                           │                          │
   │  ├─ If avatar upload:   │                           │                          │
   │  │  save to wwwroot/    │── FileStream ────────────▶│                          │
   │  │  avatars/{guid}.ext  │                           │                          │
   │  │                       │                           │                          │
   │  ├─ PUT /api/users/me/  │                           │                          │
   │  │   profile            │──────────────────────────▶│                          │
   │  │  { Name, AvatarUrl } │                           │── UPDATE users SET ────▶│
   │  │                       │                           │   name, avatar_url      │
   │  │                       │◀── OK ────────────────────│                          │
   │  │                       │                           │                          │
   │  ├─ Re-sign cookie:     │                           │                          │
   │  │  Update Name claim   │                           │                          │
   │  │  Update avatar_url   │                           │                          │
   │  │  ctx.SignInAsync()   │                           │                          │
   │  │                       │                           │                          │
   │◀─ Updated #profile-card │                           │                          │
   │   HTML (htmx swap)      │                           │                          │
```

### Why Re-sign the Cookie?

After updating the user's name or avatar, the auth cookie still carries the OLD claims. Re-signing ensures:
1. The updated name appears in the nav bar immediately (without page refresh)
2. Future Server API calls carry the correct name/avatar_url claims
3. `GetUserProfile` returns consistent data

---

## 4. Delivery Branding Manager

The right side of the Account page shows all deliveries and allows branding each one.

```
Account.razor.js                 Web (9000)                    Server (59155)
     │                               │                              │
     │  loadDeliveries()             │                              │
     │  ── GET /api/deliveries ─────▶── GET /api/deliveries ──────▶│
     │◀── JSON [{id, fileName,       │◀── JSON ─────────────────────│
     │         shareCode,            │                              │
     │         hasBranding}]         │                              │
     │                               │                              │
     │  toggleEdit(i)                │                              │
     │  ── GET /api/deliveries/      │                              │
     │     {code}/brand ────────────▶── GET .../brand ─────────────▶│
     │◀── { logo, message, accent }  │◀── JSON ─────────────────────│
     │                               │                              │
     │  saveBranding(i)              │                              │
     │  ── PUT /api/deliveries/      │                              │
     │     {id}/brand ──────────────▶── PUT .../brand ─────────────▶│
     │     { logo, message, accent } │  ── UPSERT delivery_brand ──▶│
     │◀── OK                         │◀── OK ───────────────────────│
```

### Branding Fields
| Field | Type | Description |
|-------|------|-------------|
| Logo URL | `string?` | URL to sender's logo image |
| Brand Message | `string?` | Custom message shown to downloader |
| Accent Color | `string` | Hex color (`#6366f1`) for branded elements |

### Branding Rendering (Download Page)
When a downloader visits `/d/{code}`, the download page JS fetches the brand and renders:
- Logo + message in a card above the file info
- Accent color applied to border/shadow of branding card

---

## 5. Avatar Upload Constraints

| Constraint | Value | Enforced In |
|-----------|-------|-------------|
| Max file size | 5 MB | `previewAvatar()` (JS client-side) + `HandleUpdateProfile()` (server-side) |
| Allowed types | `image/*` | `accept="image/*"` on `<input>` + JS validation |
| Storage path | `wwwroot/avatars/{guid}.{ext}` | `HandleUpdateProfile()` |
| URL format | `/avatars/{guid}.{ext}` | Saved to DB + cookie claim |

Client-side preview uses `FileReader.readAsDataURL()` to show the image before upload.

---

## 6. Auth Cookie Claims

After Google OAuth login (Web side):

| Claim Type | Value | Source |
|-----------|-------|--------|
| `ClaimTypes.NameIdentifier` | Google subject ID | OAuth callback |
| `ClaimTypes.Name` | Google display name | OAuth callback |
| `ClaimTypes.Email` | Google email | OAuth callback |
| `provider` | `"google"` | `OnCreatingTicket` |
| `avatar_url` | Google picture URL | `OnCreatingTicket` |

After profile update (re-sign):

| Claim Type | Updated To |
|-----------|-----------|
| `ClaimTypes.Name` | User-entered name |
| `avatar_url` | `/avatars/{guid}.{ext}` (or null) |

Note: `local_user_id` is NOT in the auth cookie. Server APIs derive the DB user ID via `GetOrCreateUserAsync()` which matches by email.

---

## 7. API Endpoints

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| POST | `/api/users/register` | Anonymous | `RegisterUserEndpoint` |
| GET | `/api/users/me/profile` | Required | `GetUserProfileEndpoint` |
| PUT | `/api/users/me/profile` | Required | `UpdateUserProfileEndpoint` |
| GET | `/api/deliveries` | Required | `ListDeliveriesEndpoint` |
| GET | `/api/deliveries/{code}/brand` | Required | `GetBrandEndpoint` |
| PUT | `/api/deliveries/{id}/brand` | Required | `SetBrandEndpoint` |

### Profile Response DTO
```csharp
public sealed record ProfileResponse(
    Guid Id,
    string Name,
    string Email,
    string AvatarUrl,
    string Plan,
    DateTime CreatedAt
);
```

Mapped via `UserMapper.ToProfileResponse()` (Mapperly source-gen, in `QuickShare.Shared.Models`).

---

## 8. File Reference

| File | Role |
|------|------|
| `src/QuickShare.Web/Pages/Account.razor` | Profile UI + `HandleUpdateProfile()` handler |
| `src/QuickShare.Web/Pages/Account.razor.css` | Scoped styles |
| `src/QuickShare.Web/Pages/Account.razor.js` | Avatar preview + branding manager JS |
| `src/QuickShare.Server/Features/Users/Register/RegisterUserEndpoint.cs` | `POST /api/users/register` — idempotent user creation |
| `src/QuickShare.Server/Features/Users/Profile/GetUserProfileEndpoint.cs` | `GET /api/users/me/profile` |
| `src/QuickShare.Server/Features/Users/Profile/UpdateUserProfileEndpoint.cs` | `PUT /api/users/me/profile` |
| `src/QuickShare.Server/Features/Users/Shared/UserMapper.cs` | Mapperly: `User` → `ProfileResponse` |
| `src/QuickShare.Server/Extensions/ClaimsPrincipalExtensions.cs` | `GetOrCreateUserAsync()` |
| `src/QuickShare.Shared/Models/User.cs` | User entity |
| `src/QuickShare.Server/Features/Deliveries/Brand/GetBrandEndpoint.cs` | `GET /api/deliveries/{code}/brand` |
| `src/QuickShare.Server/Features/Deliveries/Brand/SetBrandEndpoint.cs` | `PUT /api/deliveries/{id}/brand` |

---

## 9. Error States

| Error | How Detected | User Sees |
|-------|-------------|-----------|
| Profile fetch failed | `OnInitializedAsync` catch | Renders with defaults ("Unknown", no avatar) |
| Name empty | `HandleUpdateProfile` validation | `db-alert--error`: "Name is required" |
| Avatar too large (>5MB) | JS + server validation | `alert()` JS; `db-alert--error` server |
| Avatar wrong type | JS validation (`image/*`) | `alert()` JS |
| Brand save failed | `fetch()` error | `db-alert--error` inline in JS template |
| Network error | `fetch()` exception | `db-alert--error` inline |
