# Localization Gap Catalog — QuickShare

Evidence gathered 2026-08-14. Baseline: `SharedLoc.en.resx` / `SharedLoc.vi.resx` = 434 keys each, full parity, zero duplicates, zero missing either direction.

Legend: **[UNUSED]** = key exists in resx but markup hardcodes instead · **[ABSENT]** = no resx key exists, must be added EN+VI · **[JS]** = English literal in JS.

---

## A. Pages with zero `Loc[]` usage (hardcoded text)

| File | Language hardcoded | Resx keys | Status |
|---|---|---|---|
| `Components/Pages/P2PTransfer.razor` | English (whole page: headline, dropzone, share prompt, expiry note, errors) — injects Loc but uses 0 keys | `p2p_send_*` exist (vi resx 307–329) | **[UNUSED]** |
| `Components/Pages/P2PDownload.razor` | English (connecting, download btn, waiting, error, complete) — no Loc inject | `p2p_download_*` exist (vi resx 331–340) | **[UNUSED]** |
| `Components/Pages/Upgrade.razor` | **Vietnamese** (h1, payment section, PayOS copy, success/error/loading states) — no Loc inject | `upgrade_*` exist (vi resx 342–346+), `upgrade_plan_desc_*`/`upgrade_plan_summary_*` exist (vi resx 368–371) | **[UNUSED]** — EN users see Vietnamese |
| `Components/Pages/ToolsShareText.razor` (lines 4–7) | English — passes `Heading=`/`Lede=`/`PasteHint=` to ToolsLanding | `tools_sharetext_*` **none** | **[ABSENT]** |
| `Components/Pages/ToolsOnlineClipboard.razor` (lines 4–7) | English | `tools_clipboard_*` **none** | **[ABSENT]** |
| `Components/Pages/ToolsSendImagesUncompressed.razor` (lines 4–7) | **Vietnamese** | `tools_images_*` **none** | **[ABSENT]** — EN users see Vietnamese |

## B. Server-side data layer

| File | Issue | Resx keys | Status |
|---|---|---|---|
| `Features/Payments/PlanCatalog.cs` (lines 17–29) | `Description` ("Dành cho nhà sáng tạo & chuyên gia") and `Summary` ("5 GB/tệp, 50 GB dung lượng/tháng…") hardcoded **Vietnamese**; feeds `Upgrade.razor` (@planSummary), `AccountBilling.razor` (@planDescription), `SummaryCard`, Pricing | `upgrade_plan_desc_pro/studio`, `upgrade_plan_summary_pro/studio` exist with proper EN+VI | **[UNUSED]** — cross-cutting; EN users see Vietnamese plan copy |

## C. JS English literals (AGENTS.md violation: no .js carries English)

| File | Literals |
|---|---|
| `wwwroot/js/p2p/download.js` | 5 const blocks: `SIGNALING_CONNECT_TIMEOUT_MESSAGE`, `RECEIVER_HANDSHAKE_TIMEOUT_MESSAGE`, `ICE_CONNECT_TIMEOUT_MESSAGE`, `DIFFERENT_NETWORK_MESSAGE`, `SENDER_LEFT_MESSAGE` (lines 9–15); button text `'Connecting…'`/`'Download directly'` (53–54) — **overwrites server Loc text**; `'Connection failed'` (64), `'Signaling client not loaded'` (210), `'Failed to handle WebRTC offer'` (309) |
| `wwwroot/js/p2p/send.js` | Quota banner (71–74: "Daily P2P upload limit reached…", "Watch an ad to unlock…", "Sign in or upgrade…"); receiver status (143–144: "Different network", "Left mid-transfer", "Disconnected"); errors (135, 242, 257, 416); QR (512, 531) |
| `wwwroot/js/p2p/signaling.js` | onError strings (73, 83, 111, 146) — surface via p2pd/p2ps error msg |
| `wwwroot/js/p2p/peerConnection.js` | onError (178, 203, 219, 229) |
| `wwwroot/js/p2p/fileTransfer.js` | onError (38, 100, 105) |
| `wwwroot/js/p2p/fileReceiver.js` | onError (39, 45, 79, 102, 114, 186) |
| `wwwroot/js/upload.js` | `'An unknown error occurred.'` (58), `'Expires: '` (81), `'QR library not loaded.'` (310), `'QR unavailable.'` (317) |
| `wwwroot/js/ad-component.js` | `'Limit reached'` (64), `'Loading ad…'` (85), `'Watch Ad for +100MB'` (146), `'Verifying…'` (171), `'Try again'` (225) — **overwrites razor Loc button text** |

Note: `wwwroot/js/download.js` is the GOOD pattern (reads i18n JSON at lines 49/59) — replicate it.

## D. Residual hardcodes in Loc-using files

| File | Issue | Resx keys |
|---|---|---|
| `Components/Pages/AccountBilling.razor` (line 43) | `@billing.PlanDisplay Plan` — hardcoded English " Plan" suffix | `billing_period_label` exists (vi 127) — check for a `billing_plan_suffix` key; add if absent |
| `Components/Layout/MainLayout.razor` (lines 90–92) | Footer links hardcoded: "Online clipboard" (EN), "Share text" (EN), "Gửi ảnh không nén" (VI mixed) | `nav_tools_*` **none** — must add |

## E. Verify during execution (not yet read)

- `Download.razor`, `Home.razor`, `Upload.razor`, `Account*.razor`, `Login.razor` — 248 `Loc[]` calls total but may still hold scattered literals.
- `AccountDeliveries.razor` — `@evt.EventLabel` / expiry label source (server-side hardcoded?).

Note: `Pricing.razor` VERIFIED CLEAN — uses only `Loc["pricing_*"]` keys, zero `PlanCatalog`/`PlanData`/`.Features`/`.Description`/`.Summary` references. The Vietnamese PlanCatalog leak does NOT reach Pricing.

## F. Non-issues (structural, no UI text — leave untouched)

- `Components/Shared/PageWrapper.razor` (16 lines, pure layout)
- `Components/Layout/HtmxorLayout.razor` (2 lines, `@Body`)
- `Components/Pages/AdReward.razor` (delegates to AdComponent, which has Loc)
- `Components/Shared/ToolsLanding.razor` (stateless params — fix at call sites, CAP-3)

---

## Execution checklist (mapped to SPEC capabilities)

1. **[CAP-1]** P2PTransfer.razor + P2PDownload.razor → `Loc["p2p_send_*"]`/`Loc["p2p_download_*"]`; add `*-i18n` JSON blocks; purge send.js/download.js literals (quota, status, errors, button text).
2. **[CAP-2]** Upgrade.razor → `Loc["upgrade_*"]`; PlanCatalog Description/Summary → Loc lookup by plan key (`upgrade_plan_desc_{plan}`, `upgrade_plan_summary_{plan}`).
3. **[CAP-3]** Add `nav_tools_*` + `tools_*` keys to BOTH resx; Tools pages pass `@Loc[...]` into ToolsLanding; MainLayout footer links → `Loc["nav_tools_*"]`.
4. **[CAP-4]** i18n blocks for Upload (expiry label, errors, QR fallbacks) + AdComponent (button states); purge upload.js/ad-component.js literals; decide P2P protocol onError routing (surface localized message or map to localized keys).
5. **[CAP-5]** AccountBilling " Plan" suffix; sweep section E; `git grep` gate: no `>[A-Za-z]{2,}<` text nodes, no UI JS literals.
6. **[GATE]** Re-run resx parity check — both files equal key count, new keys in both; EN/VI smoke walk of the six hardcoded surfaces.