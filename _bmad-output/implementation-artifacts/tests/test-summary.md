# Test Automation Summary — 2026-08-09 (branch `dev`)

Kết quả: **Server 125/125** (~23s) · **Browser 15/15** (~31s)

## Tier 1 — In-process E2E (`tests/QuickShare.Server.Tests/E2E/`, xUnit + TestWebAppFactory)

Hạ tầng: **SQLite file per-factory** (thay InMemory) — relational thật nên `ExecuteUpdateAsync` và FK constraint chạy như production. `MigrationsAssembly` trỏ vào test assembly để skip Npgsql migrations; schema từ `EnsureCreated`.

- `UploadDownloadFlowTests` — upload→share→download, OTP, 410/403/404, plan limits (kể cả Pro 6GB), part-URL security
- `UploadResumeTests` — session cũ >15 phút vẫn xin được part URL mới (backdate, không cần fake clock); `ZombiePartSweeper` chỉ quét session quá hạn (method `internal` + InternalsVisibleTo)
- `PayOSWebhookSignatureTests` — **chữ ký HỢP LỆ** (ký bằng chính `CryptoProvider` của SDK → không thể lệch thuật toán): activate plan + complete intent; orderCode lạ → 200 ignored; sửa amount sau khi ký → 401
- `PaymentFlowTests` — intent tạo đúng, chữ ký sai 401, activation, cancel path (không webhook → plan giữ free, intent Pending)
- `AccountProfileTests` — rename qua Htmxor protocol (HXOR-Event-Handler-Id + HX-XSRF-TOKEN) **persist vào DB thật**; validation rỗng; redirect login
- `LocalizationTests` — vi/en theo culture cookie, không lộ raw key
- `DeliveryExtrasApiTests` — brand (free 403 / pro thành công), abort, P2P share API
- `P2PQuotaAndAdTests`, `AuthFlowTests`, `HtmxLazyLoadTests`, `HtmxCacheTests`, `PageLoadTests`, `PageCoverageTests`

## Tier 2 — Browser (Playwright + Kestrel, `tests/QuickShare.Browser.Tests/`, chạy tuần tự — xem AssemblyInfo.cs)

- `P2PTransferBrowserTests` — **WebRTC thật giữa 2 browser context**: sender tạo link, receiver nhận file qua RTCDataChannel, byte count khớp. Chỉ còn NAT/4G cần thiết bị thật
- `UploadResumeBrowserTests` — **mất mạng + resume** (Playwright route abort/fulfill part PUT qua `BrowserStorageStub`): retries cạn → nút Resume → resume thành công; happy path; cancel giữa chừng về idle
- `DownloadPageTests` — gate theo plan sender: Pro → file card thẳng, Free → ad countdown + skip, OTP sai/đúng, expired, not-found
- `LanguageSwitchTests` — switcher vi↔en set cookie + reload, giữ qua navigation
- `SmokeTests` — pages không JS error, HTMX swap không full reload, P2P tạo link

## Bug production tìm được nhờ test (đã fix)

1. **`RawJson` bị Razor HTML-encode trong `<script>`** (`Download.razor`) → `JSON.parse` fail âm thầm → OTP gate và ad interstitial KHÔNG BAO GIỜ hiện. Fix: `MarkupString` (an toàn XSS — STJ encoder escape `<>&`).

## Quyết định sản phẩm đã thực thi

- **Feature Report đã xoá** (issue #4 cập nhật, bullet Report gỡ): `Features/Reports/`, UI trong `Download.razor`, JS trong `download.js`, tests.

## Còn lại phải manual (checklist issues #2–#9 trước release)

NAT/4G thật (P2P), mobile Safari/Chrome thật, PayOS sandbox tiền thật, Google OAuth màn hình thật, drag&drop/clipboard cảm quan.

## Chạy

```bash
dotnet test tests/QuickShare.Server.Tests   # ~23s
dotnet test tests/QuickShare.Browser.Tests  # ~31s (cần: playwright.ps1 install chromium 1 lần)
```
