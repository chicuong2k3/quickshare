# US-CAP-6 — Plans and quotas

Source: `SPEC-quickshare-features` → CAP-6

**Story**
As a user, I want my plan to cap per-file size, monthly transfer volume, and delivery count — and I want to see it enforced — so that limits are predictable and the upgrade path is obvious.

**Acceptance criteria**
- [ ] Free/Pro/Studio tiers defined by a single limits source (`PlanTierLimits`); per-file size, monthly transfers, delivery count capped.
- [ ] Anonymous cap (100 MB, `PerkOwnerLimits`) enforced as the effective ceiling for anonymous senders; signed-in users follow `PlanTierLimits` (Q-2 resolved).
- [ ] Over-quota uploads are blocked with a clear message + upgrade CTA.
- [ ] Limits are enforced server-side (never client-trust).
- [ ] Plan limits config cache TTL 1 h+; invalidation on plan config change.

**Constraints in force**
- Limits from static-class constants — no magic numbers.
- FusionCache for plan limits (1 h+ TTL).
- Server-side enforcement (FastEndpoints).

**Test anchors**
- Unit: quota check per tier boundary (≤, >, anonymous vs signed-in).
- E2E: free user hitting cap sees block + upgrade CTA.

---

## Implementation Flow

1. **Plan source of truth** — `PlanTierLimits.InitializeCache(config)` builds a static `Dictionary<PlanTier, PlanLimits>` from config (`Anonymous` from `PerkOwnerLimits` section; Free → `PlanTierLimits.Free`; Pro/Studio from `PlanLimitsConfig`). Billing tier = `user?.Plan ?? PlanTierLimits.Anonymous`.
2. **Server-side enforcement (never client-trusted)** — every upload path re-derives `plan = user?.Plan ?? Anonymous`:
   - `StartR2Handler`: `GetMaxFileSize(plan)` — declared size over → `PlanLimitExceededException` → 402 (uploads); `MaxPartsFor = min(10_000, ceil(FileSize/MinPartBytes)+1)` guard.
   - `CommitR2Handler`: re-check **actual** object size via `GetObjectSizeAsync` (not declared parts) → over → delete object + fail.
   - `MaxDownloads = GetMaxDownloads(plan)` → `DownloadCount >= MaxDownloads` → 403.
   - `GetMaxExpiry(plan)` → `ExpiresAt` clamp (fixed per plan: Free/Anonymous 24h, Pro 30d, Studio 90d — no user choice).
3. **Effective plan on the wire** — `IsPlanTier(plan)` helper normalizes (Free = community tier); `HasUnlimitedDownloads` for Pro/Studio.
4. **Recipient-facing cap feedback** — Download page renders plan-aware messaging: anonymous recipients always have the default limits; Free recipients hitting GlobalMaxFiles / ads daily cap → blocked + upgrade CTA (see CAP-4 ad gate; `PlanTierLimits.UploadTimeoutMinutes` guards deltas).
5. **P2P quota hand-off** — CAP-7 creates the 1:1 per-guest quota entry (free tier `PerkOwnerLimits` storage ceiling) so P2P recipients don't consume cloud bandwidth.
6. **Cache** — `user:plan:{userId}` TTL 10 min (`StaticCache.Instance.GetOrSet`), invalidated on plan change (CAP-8) through `UserCacheInvalidator`.

## Code Structure

- `Infrastructure/Constants/PlanTierLimits.cs` — plan/limit registry + `InitializeCache`.
- `Features/Uploads/StartR2/StartR2Handler.cs` — file-size/part-count gates.
- `Features/Uploads/CommitR2/CommitR2Handler.cs` — actual-size re-check, max downloads, expiry.
- `Features/Downloads/Download/DownloadDeliveryHandler.cs` — download-count gate.
- `Infrastructure/Caching/UserCacheInvalidator.cs` — plan cache eviction.
- `appsettings.json` → `PlanLimits` / `PerkOwnerLimits` config sections.
