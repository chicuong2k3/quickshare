# QuickShare — Profile Architecture

## Overview

The profile system displays and updates user identity (name, avatar). Users are registered in DB at Google OAuth login time via `UserRegistrationService.RegisterAsync()` (called in-process from `OnCreatingTicket`). SSR pages access profile data in-process via `IMediator` or `AppDbContext`.

---

## 1. Component Map

| Layer | File | Role |
|-------|------|------|
| Account Page (UI) | `Pages/Account.razor` | `/account` — shell: profile card (HTMX lazy-loaded) + delivery branding manager |
| Profile Card Page | `Pages/AccountProfile.razor` | `/account/profile` — name/avatar form (`hx-post`), DB access + cookie re-sign in code-behind |
| Account CSS | `Pages/AccountProfile.razor.css` / `Pages/Account.razor.css` | Scoped styles |
| Account JS | `wwwroot/js/account.js` | Avatar preview + branding CRUD |
| Profile Form Handler | `Pages/AccountProfile.razor` (code-behind) | `HandleUpdateProfile()` — validates + submits, re-signs cookie |
| Cookie Re-sign | `Pages/AccountProfile.razor` | `ctx.SignInAsync()` after profile update |
| Nav Avatar | `Features/Users/Avatar/GetAvatarEndpoint.cs` | `GET /api/me/avatar` — avatar HTML for nav bar (HTMX) |
| User Mapper | `Features/Users/Shared/UserMapper.cs` | Mapperly source-gen: `User` → `ProfileResponse` |
| UserRegistrationService | `Features/Users/Shared/UserRegistrationService.cs` | DRY registration (all OAuth providers) |
| User DB Helper | `Extensions/ClaimsPrincipalExtensions.cs` | `GetUserAsync()` — user lookup |
| Shared DTO | `Data/Entities/ProfileResponse.cs` | `ProfileResponse` DTO |

---

## 2. Data Flow — View Profile (SSR, In-Process)

```
Browser                  QuickShare (9000)            PostgreSQL
   │                         │                          │
   │  GET /account (cookie)  │                          │
   │────────────────────────▶│                          │
   │◀─ Account.razor shell ──│  (instant, no DB calls)  │
   │  profile card hx-get    │                          │
   │  /account/profile       │                          │
   │────────────────────────▶│                          │
   │                         │  OnInitializedAsync():   │
   │                         │  GetUserAsync() → DB     │
   │                         │── DB query ─────────────▶│
   │                         │◀── User row ─────────────│
   │                         │                          │
   │◀─ profile card HTML ────│  (HTMX swap)             │
```

User is resolved via `GetUserAsync()` which matches by `provider + providerKey` in `UserLogins` table.

---

## 3. Data Flow — Update Profile

```
Browser                  QuickShare (9000)            PostgreSQL
   │                         │                          │
   │  POST /account/profile  │                          │
   │  (hx-post, hx-target=   │                          │
   │   "#profile-card")      │                          │
   │  multipart/form-data    │                          │
   │────────────────────────▶│                          │
   │                         │                          │
   │  HandleUpdateProfile()  │                          │
   │  ├─ Validate name       │                          │
   │  ├─ Validate avatar     │                          │
   │  │  (max 5MB, image/*)  │                          │
   │  ├─ If avatar upload:   │                          │
   │  │  base64-encode →     │                          │
   │  │  store as data URL   │                          │
   │  │  in AvatarUrl column │                          │
   │  ├─ AppDbContext        │                          │
   │  │  (ExecuteUpdateAsync)│── UPDATE users ─────────▶│
   │  │                       │                          │
   │  ├─ Re-sign cookie:     │                          │
   │  │  ctx.SignInAsync()   │                          │
   │  │  (Name claim only)   │                          │
   │  │                       │                          │
   │◀─ Updated #profile-card │                          │
   │   HTML (htmx swap)      │                          │
```

### Why Re-sign the Cookie?

After updating the user's name or avatar, the auth cookie still carries the OLD claims. Re-signing ensures:
1. The updated name appears in the nav bar immediately (without page refresh)
2. Future requests carry the correct name claim
3. The `profileUpdated` HX-Trigger refreshes the nav avatar via `GET /api/me/avatar`

---

## 4. User Registration Flow

Users are registered at **Google OAuth login time** by `OnCreatingTicket`, which calls `UserRegistrationService.RegisterAsync()` in-process. (The programmatic `POST /api/users/register` endpoint was removed — OAuth is the only registration path.)

```
OnCreatingTicket                          UserRegistrationService
     │                                         │
     ├─ RegistrationService.RegisterAsync(     │
     │   provider, providerKey, name,          │
     │   email, avatar, db, ct) ──────────────▶│
     │                                         ├─ Lookup by provider + providerKey
     │                                         ├─ If found: update name/email, return user
     │                                         ├─ If not: create User + UserLogin + UserUsage
     │                                         └─ Return ProfileResponse
```

**User resolution on every authenticated request (`GetUserAsync`):**

```csharp
// ClaimsPrincipalExtensions.cs
public static async Task<User> GetUserAsync(this ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
{
    // Looks up by provider + providerKey in UserLogins table
    // Throws InvalidOperationException if user not found (must be registered first)
}
```

---

## 5. Delivery Branding Manager

The right side of the Account page shows all deliveries and allows branding each one. JS fetches data from `/api/deliveries` and `/api/deliveries/{code}/brand` endpoints (same-origin, relative URLs).

### Branding Fields
| Field | Type | Description |
|-------|------|-------------|
| Logo URL | `string?` | URL to sender's logo image |
| Brand Message | `string?` | Custom message shown to downloader |
| Accent Color | `string` | Hex color (`#6366f1`) for branded elements |

---

## 6. Avatar Upload Constraints

| Constraint | Value | Enforced In |
|-----------|-------|-------------|
| Max file size | 5 MB | JS client-side + server-side validation (`HandleUpdateProfile`) |
| Allowed types | `image/*` | `accept="image/*"` on `<input>` + JS validation |
| Storage | `users.AvatarUrl` column | `HandleUpdateProfile()` — base64 data URL (`data:{mime};base64,...`) |
| URL format | Stored value used directly as `<img src>` | Saved to DB |

---

## 7. Auth Cookie Claims

After Google OAuth login (claims set by the OAuth pipeline + `OnCreatingTicket`):

| Claim Type | Value | Source |
|-----------|-------|--------|
| `ClaimTypes.NameIdentifier` | Google subject ID | OAuth callback |
| `ClaimTypes.Name` | Google display name | OAuth callback |
| `ClaimTypes.Email` | Google email | OAuth callback |
| `provider` | `"google"` | `OnCreatingTicket` |

> No `avatar` or `plan` claims are stored. Avatar + plan are read from the DB on each request (`GetUserAsync()` → `ProfileResponse`); the nav avatar uses `GET /api/me/avatar`.

After profile update (re-sign):

| Claim Type | Updated To |
|-----------|-----------|
| `ClaimTypes.Name` | User-entered name |

---

## 8. API Endpoints (branding + nav avatar — called from JS/HTMX)

Profile data is loaded in-process via `AppDbContext` or `IMediator` in `Account.razor` / `AccountProfile.razor`. Profile mutations use `hx-post` form handlers. The account page also uses these JS/HTMX-called endpoints for delivery branding and the nav avatar:

| Method | Path | Auth | Handler | Consumer |
|--------|------|------|---------|----------|
| GET | `/api/deliveries` | Required | `ListDeliveriesEndpoint` | account.js |
| GET | `/api/deliveries/{code}/brand` | Required | `GetBrandEndpoint` | account.js, download.js |
| PUT | `/api/deliveries/{id}/brand` | Required | `SetBrandEndpoint` | account.js |
| GET | `/api/me/avatar` | Required | `GetAvatarEndpoint` | MainLayout nav (HTMX) |

---

## 9. File Reference

| File | Role |
|------|------|
| `src/QuickShare/Pages/Account.razor` | `/account` shell — HTMX profile card + branding manager |
| `src/QuickShare/Pages/AccountProfile.razor` | `/account/profile` — name/avatar form (`hx-post`) + cookie re-sign |
| `src/QuickShare/Pages/Account.razor.css` | Scoped styles |
| `src/QuickShare/Pages/AccountProfile.razor.css` | Scoped styles |
| `src/QuickShare/wwwroot/js/account.js` | Avatar preview + branding manager JS |
| `src/QuickShare/Features/Users/Avatar/GetAvatarEndpoint.cs` | `GET /api/me/avatar` — nav avatar HTML |
| `src/QuickShare/Features/Users/Shared/UserRegistrationService.cs` | DRY registration (all OAuth providers) |
| `src/QuickShare/Features/Users/Shared/UserMapper.cs` | Mapperly: `User` → `ProfileResponse` |
| `src/QuickShare/Extensions/ClaimsPrincipalExtensions.cs` | `GetUserAsync()` |
| `src/QuickShare/Data/Entities/ProfileResponse.cs` | `ProfileResponse` DTO |
| `src/QuickShare/Features/Deliveries/Brand/GetBrandEndpoint.cs` | `GET /api/deliveries/{code}/brand` |
| `src/QuickShare/Features/Deliveries/Brand/SetBrandEndpoint.cs` | `PUT /api/deliveries/{id}/brand` |

---

## 10. Error States

| Error | How Detected | User Sees |
|-------|-------------|-----------|
| Profile fetch failed | `OnInitializedAsync` catch | Renders with defaults ("Unknown", no avatar) |
| Name empty | `HandleUpdateProfile` validation | Error alert: "Name is required" |
| Avatar too large (>5MB) | JS + server validation | JS alert; server error alert |
| Avatar wrong type | JS validation (`image/*`) | JS alert |
| Brand save failed | `fetch()` error | Inline error alert |
| Network error | `fetch()` exception | Inline error alert |
