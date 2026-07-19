# PRD — TeleCloud

> Một trang, drop vào `bmad-prd`. Tổng hợp toàn bộ quyết định từ phiên brainstorm (memlog 133+ entries).

## 1. Sản phẩm (one-liner)
**TeleCloud = "Telegram không có trần 2GB."** Gửi file bất kỳ dung lượng qua Telegram, auto-chunk, 1 chạm — Telegram chặn 2GB/file, TeleCloud không.

## 2. Wedge & khách hàng
- **Wedge duy nhất**: file đơn **>2GB, free, qua Telegram, không cần tự split**. (Telegram cấu trúc không làm được.)
- **Hạ làm feature (không đặt cược)**: receiver không cần TG (#1 — yếu vì VN ai cũng có messenger); ephemeral/E2E (#3 — big-file user không cần privacy).
- **Khách trả tiền**: power user đụng 2GB hoài (creator/editor/dev gửi build/gamer-ROM) + người đang phải trả WeTransfer/TG-Premium cho >2GB.
- **Nhường có chủ đích**: gửi <2GB TG→TG (Telegram OK) và bulk-pirate-to-TG-buddy (không trả tiền + là nguồn ban).

## 3. Kiến trúc (tóm tắt)
- **MTProto gateway** (Telethon/Pyrogram, Python) = nơi duy nhất giữ session Telegram. Client không bao giờ thấy credentials.
- **Forum supergroup** private làm kho; **1 topic/file**; takedown = xóa topic.
- **Chunking**: 512MB/chunk (≤2GB cứng), upload/download song song, resume mức chunk, SHA-256 từng chunk + toàn file, commit-khi-đủ.
- **E2E defensive**: encrypt-before-chunk (AES-256-GCM độc lập, AAD = manifest_id+index); key trong URL fragment (vô hình UX); manifest encrypt. **Vì blob mã hóa Telegram không content-flag được → giảm ban. Không headline "private".**
- **Sender gating**: receiver không login; sender Guest(ẩn danh+rate-limit)/Verified(Google 1-tap)/Paid. Quota hero = single-file (Verified ~50GB+); volume/ngày cap để sống.

## 4. MVP scope (v1)
**Trong MVP:**
- Chunking: 512MB, parallel (tier), resume, SHA-256 verify, commit-khi-đủ.
- Encrypt-before-chunk + manifest encrypt + key-trong-link.
- Gateway: `begin/chunk/commit` + MTProto (Telethon) + Redis quota + identity DB tối giản.
- Sender tiers + Google Sign-In 1-tap (Verified).
- **Guest được gửi 1 file >2GB miễn phí** (demo convert).
- Ephemeral mặc định + report→takedown theo `manifest_id`.
- Share-link (short-link) + Android share-sheet 1 chạm + VI UI.

**Ngoài MVP (sau):** Zalo OAuth · OTP-via-TG · paid-tier integration · owned-storage (S3/VPS) migration · desktop/CLI/browser ext · folder send · stream-as-you-go.

## 5. Rủi ro & giảm thiểu
| Rủi ro | Giảm thiểu |
|---|---|
| **ToS/ban MTProto** (tồn tại) | Obey FLOOD_WAIT (backoff); volume ceiling/account; ephemeral mặc định; encrypt (blind pipe); identity-gate phía gửi; 1 account ở MVP (dừng nhận mới khi tới trần, KHÔNG xoay account); revenue → owned storage |
| **E2E = không content-moderation** | Moderation metadata+identity+rate+report→takedown; nói thật với user, đừng oversell |
| **Single account bottleneck** | Dừng nhận mới + đứng hàng khi tới trần; migration owned-storage |
| **Patch client vượt quota** | Enforce server-side (gateway), không tin client |

## 6. Success metrics (đo wedge thật)
- **% file gửi >2GB** (validation wedge — phải là phần lớn有意义).
- **Upload success rate** + tần suất **FLOOD_WAIT** / sự cố ban.
- **Conversion Guest→Verified→Paid**.
- **Time-to-first->2GB-send** của user mới (đo khoảnh khắc convert).

## 7. Open questions
- Benchmark chunk-size × concurrency trên mạng VN (chốt default 512MB / concurrency 4?).
- Sweet spot volume-ceiling/account để dưới ngưỡng ban.
- Mô hình giá Paid cụ thể (pay-per-GB vs subscription) — A/B sau khi có data.
- Kịch bản migration owned-storage khi nào trigger (volume / first ban).

## 8. Nguyên tắc đặt nền
- **1 đường, không xếp lớp lách** (no fallbacks): không xoay account, không "chế độ scan" song song.
- **Closed source + freemium**: giữ source để thu phí; trust qua zero-knowledge-by-construction + paid audit + bug bounty, KHÔNG oversell.
- **Proof not promises**: đừng lặp lại sai lầm oversell "secure/anonymous" của app bị roast.
