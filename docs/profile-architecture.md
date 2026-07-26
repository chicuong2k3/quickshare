# QuickShare — Profile Architecture

## Overview

The profile system displays and updates user identity (name, avatar). Users are registered in DB at Google OAuth login time via `UserRegistrationService.RegisterAsync()` (called in-process from `OnCreatingTicket`). SSR pages access profile data in-process via `IMessageBus` or `AppDbContext`.

---

## 1. Component Map

| Layer | File | Role |
|-------|------|------|
| Account Page (UI) | `Pages/Account.razor` | Profile card + delivery branding manager |
| Account CSS | `Pages/Account.razor.css` | Scoped styles |
| Account JS | `wwwroot/js/account.js` | Avatar preview + branding CRUD |
| Profile Form Handler | `Pages/Account.razor` (code-behind) | `<form method="post">` handler — validates + submits, re-signs cookie |
| Cookie Re-sign | `Pages/Account.razor` | `ctx.SignInAsync()` after profile update |
| User Mapper | `Features/Users/Shared/UserMapper.cs` | Mapperly source-gen: `User` → `ProfileResponse` |
| UserRegistrationService | `Features/Users/Shared/UserRegistrationService.cs` | DRY registration (all OAuth providers) |
| User DB Helper | `Extensions/ClaimsPrincipalExtensions.cs` | `GetUserAsync()` — user lookup |
| Shared DTO | `Models/ProfileResponse.cs` | `ProfileResponse` DTO |

---

## 2. Data Flow — View Profile (SSR, In-Process)

```
Browser                  QuickShare (9000)            PostgreSQL
   │                         │                          │
   │  GET /account (cookie)  │                          │
   │────────────────────────▶│                          │
   │                         │                          │
   │  OnInitializedAsync():  │                          │
   │  Inject IMessageBus     │                          │
   │  or AppDbContext        │                          │
   │  (in-process query)     │                          │
   │                         │── DB query ─────────────▶│
   │                         │◀── User row ─────────────│
   │                         │                          │
   │◀─ Account.razor HTML ───│                          │
   │   + profile data        │                          │
```

User is resolved via `GetUserAsync()` which matches by `provider + providerKey` in `UserLogins` table.

---

## 3. Data Flow — Update Profile

```
Browser                  QuickShare (9000)            PostgreSQL
   │                         │                          │
   │  POST /account          │                          │
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
   │  │  save to wwwroot/    │                          │
   │  │  avatars/{guid}.ext  │                          │
   │  ├─ IMessageBus or      │                          │
   │  │  AppDbContext         │                          │
   │  │  (in-process update) │── UPDATE users ─────────▶│
   │  │                       │                          │
   │  ├─ Re-sign cookie:     │                          │
   │  │  ctx.SignInAsync()   │                          │
   │  │                       │                          │
   │◀─ Updated #profile-card │                          │
   │   HTML (htmx swap)      │                          │
```

### Why Re-sign the Cookie?

After updating the user's name or avatar, the auth cookie still carries the OLD claims. Re-signing ensures:
1. The updated name appears in the nav bar immediately (without page refresh)
2. Future requests carry the correct name/avatar_url claims
3. `GetUserProfile` returns consistent data

---

## 4. User Registration Flow

Users are registered at **Google OAuth login time** by `OnCreatingTicket`, which calls `UserRegistrationService.RegisterAsync()` in-process. The same service is used by `POST /api/users/register` for any programmatic registration.

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
| Max file size | 5 MB | JS client-side + server-side validation |
| Allowed types | `image/*` | `accept="image/*"` on `<input>` + JS validation |
| Storage path | `wwwroot/avatars/{guid}.{ext}` | `HandleUpdateProfile()` |
| URL format | `/avatars/{guid}.{ext}` | Saved to DB + cookie claim |

---

## 7. Auth Cookie Claims

After Google OAuth login:

| Claim Type | Value | Source |
|-----------|-------|--------|
| `ClaimTypes.NameIdentifier` | Google subject ID | OAuth callback |
| `ClaimTypes.Name` | Google display name | OAuth callback |
| `ClaimTypes.Email` | Google email | OAuth callback |
| `provider` | `"google"` | `OnCreatingTicket` |
| `avatar_url` | Google picture URL | `OnCreatingTicket` |
| `plan` | User's plan tier | `UserRegistrationService.RegisterAsync()` |

After profile update (re-sign):

| Claim Type | Updated To |
|-----------|-----------|
| `ClaimTypes.Name` | User-entered name |
| `avatar_url` | `/avatars/{guid}.{ext}` (or null) |

---

## 8. API Endpoints (delivery branding — called from JS)

Profile data is loaded in-process via `AppDbContext` or `IMessageBus` in `Account.razor`. Profile mutations use `<form method="post">` handlers. No dedicated user profile API endpoints exist. The account page also uses these JS-called endpoints for delivery branding:

| Method | Path | Auth | Handler | Consumer |
|--------|------|------|---------|----------|
| GET | `/api/deliveries` | Required | `ListDeliveriesEndpoint` | account.js |
| GET | `/api/deliveries/{code}/brand` | Required | `GetBrandEndpoint` | account.js |
| PUT | `/api/deliveries/{id}/brand` | Required | `SetBrandEndpoint` | account.js |

---

## 9. File Reference

| File | Role |
|------|------|
| `src/QuickShare/Pages/Account.razor` | Profile UI + `<form method="post">` handler |
| `src/QuickShare/Pages/Account.razor.css` | Scoped styles |
| `src/QuickShare/wwwroot/js/account.js` | Avatar preview + branding manager JS |
| `src/QuickShare/Features/Users/Shared/UserRegistrationService.cs` | DRY registration (all OAuth providers) |
| `src/QuickShare/Features/Users/Shared/UserMapper.cs` | Mapperly: `User` → `ProfileResponse` |
| `src/QuickShare/Extensions/ClaimsPrincipalExtensions.cs` | `GetUserAsync()` |
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
