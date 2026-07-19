# MTProto Backend Spec — TeleCloud

> Chọn cách gateway nói chuyện với Telegram. Nguyên tắc đã chốt: **chỉ gateway giữ session MTProto**, client không bao giờ thấy credentials Telegram (chống abuse + đúng gating).

## Lựa chọn thư viện
| Option | Mô tả | Phù hợp? |
|---|---|---|
| **Telethon / Pyrogram (Python)** | Userbot server-side, dễ, docs tốt, active | ✅ Đề xuất cho solo dev — ship nhanh |
| **TDLib (C++, binding Kotlin/Java/Python)** | Lib chính thức Telegram, xử lý MTProto/session/file đầy đủ | ⚠ Nặng, thiết kế như "client đầy đủ"; hợp khi cần scale/perf |
| **Raw MTProto trong Kotlin (client)** | Client tự nói MTProto | ❌ Loại — client sẽ giữ session TG → lộ account cho mọi user |

**Chốt**: Gateway Python + **Telethon/Pyrogram** để ship nhanh; migrate TDLib sau nếu cần. Android client = UI mỏng + crypto + HTTP tới gateway.

> Lưu ý pháp lý/ToS (nói thẳng): ToS API Telegram về bản chất **cấm dùng API để xây dịch vụ lưu trữ/chia sẻ file cho bên thứ 3**. Đây là rủi ro tồn tại, không phải bug. Thiết kế cho sự mong manh: ephemeral mặc định, không hứa vĩnh viễn, plan migrate sang owned storage khi có revenue.

## Layout kênh (private "drive")
- **1 supergroup forum riêng** (private) làm kho; **1 topic/file**.
- Pour các chunk vào topic đó; manifest cũng 1 message trong topic.
- **Takedown = xóa topic** (sạch, 1 thao tác, theo `manifest_id`, không cần decrypt).
- Tránh: 1 channel chung lộn xộn (khó xóa từng file); 1 channel/user (vượt giới hạn).

## Quản lý file / session
- **File references** MTProto hết hạn → khi download, re-get message để refresh `file_ref` (Telethon tự handle; không phải lỗi).
- Mỗi Telegram account = 1 TDLib/Telethon **client instance** trên gateway; nhiều user multiplex qua đó.
- **Một account duy nhất** ở MVP = single point ban-target (chấp nhận ở bootstrap; lý do phải rate-limit + ephemeral gắt).

## Hiệu năng trên mạng VN (cần đo thực tế)
- DC gần nhất của Telegram từ VN có latency nhất định → đo throughput thật.
- Đo biến: **chunk-size** (128/256/512MB) × **concurrency** (1/2/4/8) → tìm sweet spot.
- Sweet spot thường **concurrency 4–8**; vượt mức đó = diminishing + risk `FLOOD_WAIT`.

## Tuân thủ Telegram (chống ban) — bắt buộc
1. **Obey `FLOOD_WAIT`**: exponential backoff, KHÔNG cố push. Đây là tín hiệu rate của Telegram.
2. **Volume ceiling tự áp** / account / ngày (dưới ngưỡng ban).
3. **Không pattern spam**: tránh burst đều đặn, randomize nhẹ, giới hạn tần suất.
4. **Ephemeral mặc định**: auto-delete topic theo expiry → giảm "dấu vết" lưu trữ.
5. **Encrypt mọi thứ** (blind pipe): ciphertext không bị content-flag.
6. **Identity-gate phía gửi** (xem sender-gating-spec): control ai/ cái-gì tới account.

## Open
- Benchmark VN thực tế (chunk-size × concurrency) trước khi chốt default 512MB/concurrency 4.
- Chiến lược khi 1 account tới trần: dừng nhận mới (đứng hàng) thay vì xoay account (no-rotation đã chốt).
- Migration plan owned-storage (S3/VPS) khi revenue đủ — đó là lối thoát ToS thật.
