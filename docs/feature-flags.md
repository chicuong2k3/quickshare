# QuickShare — Feature Flags Architecture

## Overview

Feature flags let a capability ship dark and roll out safely: the default state lives in config, an admin can flip it or set a percentage rollout at runtime, and a kill switch turns every flag off at once. Flags are **not yet wired into any feature code path** — this is the infrastructure (issue #91) that later issues (#21, #26, #44, #48) will consume.

The system has three layers:

1. **Config defaults** — `FeatureFlags` section in `appsettings.json` (name, description, on/off, rollout percent).
2. **DB overrides** — `feature_flag_overrides` table; an admin toggles/rolls out via the admin UI or `PUT /api/admin/flags/{Key}`.
3. **Admin gate** — a config list of admin emails (`Admin:Emails`) that controls who may view `/admin/flags` and call the flag endpoints. No schema change; a full admin role/claims system is tracked separately in #61.

---

## 1. Component Map

| Layer | File | Role |
|-------|------|------|
| Flag catalog | `Infrastructure/FeatureFlags/FeatureFlagKeys.cs` | Wire-format keys as `const string` (`previews`, `zero-knowledge`, `password-protection`, `virus-scan`) |
| Config model | `Infrastructure/FeatureFlags/FeatureFlagSettings.cs` | `FeatureFlags:Enabled` kill switch + `FeatureFlags:Flags` dictionary (`FeatureFlagConfig`) |
| Admin config | `Infrastructure/FeatureFlags/AdminOptions.cs` | `Admin:Emails` string[] — who may manage flags |
| Service | `Infrastructure/FeatureFlags/FeatureFlagService.cs` | Resolves effective state: config → override → kill switch → rollout hash bucket |
| Read model | `Infrastructure/FeatureFlags/FeatureFlagStatus.cs` | Per-flag view: defaults, override, effective state (admin UI + list API) |
| Admin gate | `Infrastructure/FeatureFlags/AdminGate.cs` | `IsAdmin(ClaimsPrincipal, AdminOptions)` — email-claim membership check |
| Entity | `Data/Entities/FeatureFlagOverride.cs` | DB override row (`Key` unique, `Enabled`, `RolloutPercent`), inherits `BaseAuditEntity` |
| Migration | `Data/Migrations/20260815050241_AddFeatureFlagOverrides.cs` | Creates `feature_flag_overrides` + unique index on `Key` |
| List API | `Features/Admin/Flags/ListFlagsEndpoint.cs` | `GET /api/admin/flags` — all flags with effective state |
| Update API | `Features/Admin/Flags/UpdateFlagEndpoint.cs` | `PUT /api/admin/flags/{Key}` — set override; 400 on unknown key / invalid percent |
| Admin page | `Components/Pages/Admin/Flags.razor` | `/admin/flags` — SSR table and native forms for toggles and rollout percentages |
| Tests | `tests/QuickShare.Server.Tests/Infrastructure/FeatureFlagServiceTests.cs` | Unit: resolution precedence, kill switch, rollout buckets, override persistence (EF InMemory) |
| Tests | `tests/QuickShare.Server.Tests/E2E/AdminFlagsApiTests.cs` | E2E: 401/403 gating, list, override persist + restore, 400s, page smoke |

---

## 2. Decision Flow — Effective Flag State

```
IsEnabledAsync(key, userId)
│
├─ FeatureFlags:Enabled == false          → false  (kill switch wins over everything)
│
├─ key not in FeatureFlags:Flags          → false  (unknown flag)
│
├─ DB override exists for key?
│     yes → effective = override.Enabled / override.RolloutPercent
│     no  → effective = config.Enabled / config.RolloutPercent
│
├─ effective.Enabled == false             → false
│
├─ effective.RolloutPercent >= 100        → true
│
├─ userId == null (anonymous)             → false  (partial rollout needs a stable user id)
│
└─ HashBucket(userId, key) < RolloutPercent → true
```

**Rollout bucket:** `SHA256("{userId:N}:{key}")` → first 8 bytes → `ulong % 100`. A user lands in the same bucket for a given flag every time, so rollout is deterministic per user and the "on" set grows monotonically as the percent rises.

**Resolution precedence:** override > config default; kill switch beats both. `FeatureFlagStatus` exposes all layers so the admin UI shows *why* a flag is on/off (default vs. override).

**Caching:** reads go through `CacheService` (FusionCache) with invalidation on every override write; `FusionCache:Enabled=false` (used by tests) bypasses caching entirely.

---

## 3. Admin Surface

### Admin gate

`AdminGate.IsAdmin()` checks `ClaimTypes.Email` against `Admin:Emails`. The page applies `[Authorize]` + the email gate in the shell; the endpoints rely on FastEndpoints' default auth (401 anonymous) plus the gate (403 non-admin).

### API

| Method | Route | Auth | Behavior |
|--------|-------|------|----------|
| `GET` | `/api/admin/flags` | admin | All flags with `Default*`, `Override*`, `Effective*` fields (PascalCase) |
| `PUT` | `/api/admin/flags/{Key}` | admin | Body `{ "enabled": bool, "rolloutPercent": int }`; 204 on success; 400 unknown key or percent outside 0–100 |

### Page

`/admin/flags` renders the authorized flag table on the initial GET. Each row posts to an admin-gated form endpoint that validates antiforgery, the flag key and rollout percentage before calling the same override service as the API. The form redirects back to the page and works without JavaScript.

---

## 4. Configuration

```jsonc
// appsettings.json
{
  "FeatureFlags": {
    "Enabled": true,                        // kill switch — false turns EVERY flag off
    "Flags": {
      "previews":            { "Enabled": false, "RolloutPercent": 100, "Description": "..." },
      "zero-knowledge":      { "Enabled": false, "RolloutPercent": 100, "Description": "..." },
      "password-protection": { "Enabled": true,  "RolloutPercent": 100, "Description": "..." },
      "virus-scan":          { "Enabled": false, "RolloutPercent": 100, "Description": "..." }
    }
  },
  "Admin": {
    "Emails": []                            // admins who may manage flags (also via ADMIN_EMAILS env)
  }
}
```

Admin emails can be provided via the `ADMIN_EMAILS` environment variable (comma-separated) — see `.env.example`.

---

## 5. Adding a Flag

1. Add a key to `FeatureFlagKeys` (`const string`, wire format — the value is what hits the DB and API).
2. Add the default entry under `FeatureFlags:Flags` in `appsettings.json`.
3. Add the key to the flag-keys test fixture so unit coverage stays honest (all catalog keys must resolve).
4. When the feature is ready: consume `IFeatureFlagService.IsEnabledAsync(FeatureFlagKeys.X, userId)` at the feature's entry point — then flip it from `/admin/flags` instead of redeploying.

---

## 6. Tests

- **Unit (`FeatureFlagServiceTests`)** — EF InMemory + a disabled FusionCache so every read is fresh: kill switch beats override; override beats config; unknown key → false; partial rollout deterministic per user; anonymous never gets a partial rollout; `SetOverrideAsync` updates in place (unique key) and `ListAsync` reflects effective state.
- **E2E (`AdminFlagsApiTests`)** — anonymous → 401 API / 302 page; non-admin → 403; admin lists flags; a PUT override persists and is visible via both the API and `IFeatureFlagService`, then is restored so shared-factory state stays clean; unknown key and percent >100 → 400.

Run: `dotnet test tests/QuickShare.Server.Tests`
