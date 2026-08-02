# Pricing Revamp Implementation Plan

**Date:** 2026-08-02
**Spec:** `_bmad-output/specs/spec-pricing-page/SPEC.md`
**Status:** Ready

## Summary

Replace hardcoded tier limits with config-driven `TimeSpan`-based expiry. Update Free tier to "instant delivery" model (5 GB/file, 2h TTL). Update all UI surfaces to reflect new limits and competitive messaging (lossless, speed, no-recipient-account).

## Files Affected

| File | Change | Risk |
|---|---|---|
| `src/QuickShare/Infrastructure/PlanTierLimits.cs` | Major refactor — config-driven, TimeSpan expiry | Medium |
| `src/QuickShare/Features/Uploads/CommitR2/CommitR2Handler.cs` | `.AddDays(...)` → `.Add(TimeSpan)` | Low |
| `src/QuickShare/Features/Uploads/StartR2/StartR2Handler.cs` | `.AddDays(...)` → `.Add(TimeSpan)` | Low |
| `src/QuickShare/Pages/Pricing.razor` | Update feature lists, limits in marketing copy | Low |
| `src/QuickShare/Pages/AccountBilling.razor` | Update `planFeatures` strings | Low |
| `src/QuickShare/Pages/Home.razor` | Update marketing copy (file size, expiry range, lossless) | Low |
| `src/QuickShare/appsettings.json` | Add `Pricing:Limits` section | Low |

## Step 1: Refactor `PlanTierLimits.cs`

**Current state:** Hardcoded limits. `GetMaxExpiryDays(plan)` returns `int`. `GetMaxFileSize(plan)` returns `long?`. `GetMonthlyLimits(plan)` returns tuple.

**Target state:** Config-driven. `GetMaxExpiry(plan)` returns `TimeSpan`. All limits read from `IConfiguration` with fallback defaults.

Changes:
1. Add constructor injection or static init with `IConfiguration`
2. Add `GetMaxExpiry(string plan)` → `TimeSpan` (replaces `GetMaxExpiryDays`)
3. Keep `GetMaxExpiryDays` as deprecated forwarding to new method for backward compat (remove after migration)
4. Update all limit values to match new tiers
5. Add config keys as constants

Config path pattern: `Pricing:Limits:<Free|Pro|Studio>:<Key>`

```csharp
public static class PlanTierLimits
{
    // ... existing constants ...

    private static IConfiguration? _config;

    public static void Initialize(IConfiguration config) => _config = config;

    public static TimeSpan GetMaxExpiry(string plan)
    {
        return (plan ?? string.Empty).ToLowerInvariant() switch
        {
            Free => TimeSpan.FromHours(GetConfigDouble(Free, "MaxExpiryHours", 2.0)),
            Pro => TimeSpan.FromDays(GetConfigInt(Pro, "MaxExpiryDays", 30)),
            Studio => TimeSpan.FromDays(GetConfigInt(Studio, "MaxExpiryDays", 90)),
            _ => TimeSpan.FromHours(GetConfigDouble(Free, "MaxExpiryHours", 2.0)),
        };
    }

    // ... similar for GetMaxFileSize, GetMonthlyLimits, etc. ...
}
```

**Config defaults (hardcoded fallback if section missing):**

| Key | Free | Pro | Studio |
|---|---|---|---|
| MaxFileSizeBytes | 5_368_709_120 (5 GB) | 21_474_836_480 (20 GB) | 107_374_182_400 (100 GB) |
| MaxMonthlyBytes | 16_106_127_360 (15 GB) | 214_748_364_800 (200 GB) | 2_199_023_255_552 (2 TB) |
| MaxExpiryHours | 2.0 | — | — |
| MaxExpiryDays | — | 30 | 90 |
| MaxDownloads | 10 | 100 | int.MaxValue |
| MaxConcurrency | 2 | 5 | 10 |

## Step 2: Update Handlers

`CommitR2Handler.cs:50`:
```csharp
// Before
ExpiresAt = DateTime.UtcNow.AddDays(PlanTierLimits.GetMaxExpiryDays(user.Plan)),
// After
ExpiresAt = DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(user.Plan)),
```

`StartR2Handler.cs:46,55`:
```csharp
// Before
var maxExpiryDays = PlanTierLimits.GetMaxExpiryDays(user.Plan);
ExpiresAt = DateTime.UtcNow.AddDays(maxExpiryDays),
// After
var maxExpiry = PlanTierLimits.GetMaxExpiry(user.Plan);
ExpiresAt = DateTime.UtcNow.Add(maxExpiry),
```

## Step 3: Update UI — Pricing.razor

Update feature lists per tier card:

**Free card:**
- "**5 GB** per file" (was "100 MB")
- "**15 GB** / month" (was "2 GB")
- "**10** transfers/month" (was "5")
- "Files expire after **2 hours**" (was "7 days")
- "**Lossless** quality — no compression" (new)
- "End-to-end **encryption**" (keep)

**Pro card:**
- "**20 GB** per file" (was "1 GB" — also fixing existing bug where UI says 1GB but code says 2GB)
- "**200 GB** / month" (was "50 GB")
- "**100** transfers/month" (was "100" — keep)
- "**30-day** expiry" (new)
- "**Password** protection" (keep)
- "**No ads**" (new)

**Studio card:**
- "**100 GB** per file" (was "20 GB")
- "**2 TB** / month" (was "500 GB")
- "**Unlimited** transfers" (keep)
- "**90-day** expiry" (new)
- "**Custom** branding" (keep)
- "**API** access" (keep)

## Step 4: Update UI — AccountBilling.razor

`planFeatures` switch at line 144-148:
```csharp
PlanTierLimits.Pro => new[] { "20 GB per file", "200 GB / month", "100 transfers/month", "30-day expiry", "Password protection", "No ads" },
PlanTierLimits.Studio => new[] { "100 GB per file", "2 TB / month", "Unlimited transfers", "90-day expiry", "Custom branding", "API access", "Dedicated support" },
_ => new[] { "5 GB per file", "15 GB / month", "10 transfers/month", "2-hour expiry", "Lossless quality", "End-to-end encryption" }
```

## Step 5: Update UI — Home.razor

Line 112: "Set expiration from 24 hours to 30 days" → "Free transfers auto-expire after 2 hours. Pro and Studio keep files for 30–90 days."

Line 138: "Drag and drop any file up to 200 MB" → "Drag and drop any file up to 5 GB"

Add lossless messaging near encryption section: "Files are never compressed or re-encoded — what you send is byte-for-byte what they receive."

## Step 6: Update appsettings.json

Add `Pricing:Limits` section (see config structure in pricing-tiers.md).

## Step 7: Initialize PlanTierLimits in Program.cs

```csharp
PlanTierLimits.Initialize(builder.Configuration);
```

## Verification

1. `dotnet build` — zero errors
2. Smoke test: upload a file as free user, verify `ExpiresAt` = now + 2 hours
3. Smoke test: upload as Pro user, verify `ExpiresAt` = now + 30 days
4. Pricing page renders correct VND prices and feature lists
5. Home page shows updated copy
