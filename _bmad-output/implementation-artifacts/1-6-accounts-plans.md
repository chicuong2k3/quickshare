# Story 1.6: Accounts & Plans

- **Epic:** Epic 1 — Core Delivery Pipeline
- **Story ID:** 1.6
- **Status:** ready-for-dev
- **Dependencies:** Story 1.1 ✅, Story 1.2 ✅

---

## Implementation

### 1. Update PlanTierLimits (`src/TeleCloud.Server/Data/PlanTierLimits.cs`)

Add per-file limits and plan metadata:

| Plan | MaxFileSize | MaxExpiryDays | MaxDownloads | MaxConcurrency |
|---|---|---|---|---|
| Free | 5 GB | 3 | 5 | 2 |
| Starter | 50 GB | 30 | 50 | 4 |
| Ultimate | unlimited | 30 | unlimited | 6 |

### 2. Update StartUploadHandler

Use plan-based limits from PlanTierLimits instead of hardcoded 5GB constant.

### 3. Update UploadConstants

Remove hardcoded `FreePlanMaxFileSize`; use PlanTierLimits instead.
