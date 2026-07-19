# Sender Gating Spec — TeleCloud (Telegram-backed E2E file share)

> Một đòn duy nhất giải quyết 2 việc: **chống abuse** + **giảm risk ban Telegram** — bằng cách kiểm soát *cái gì* và *ai* được chạm tới account Telegram của bạn.
> Kiến trúc: MTProto userbot + chunking + **E2E-before-upload** (key trong share-code) + Telegram-as-encrypted-DB.

## Nguyên tắc thiết kế
1. **Bất đối xứng**: người **NHẬN không bao giờ login** (wedge vs LocalSend/Telegram-direct). Chỉ người **GỬI** có identity + quota.
2. **E2E giữ nguyên**: gateway chỉ thấy ciphertext → **không scan nội dung**. Moderation dựa vào metadata + identity + rate + report, KHÔNG phải content.
3. **Enforce phía gateway, không tin client**: client có thể bị patch → quota PHẢI check ở server.
4. **1 đường, không xếp lớp**: không có "chế độ scan" song song (no-fallbacks). E2E là E2E.

> ⚠ Đính chính: ý "hash-check nội dung phi pháp trước khi upload" không khả thi dưới E2E (gateway không thấy plaintext). Moderation thật phải là metadata + identity + rate + report (xem mục E). Đây cũng là trade-off của Signal/wormhole.

## A. Identity tiers
| Tier | Identity | Cách lấy |
|---|---|---|
| **Guest** (ẩn danh) | device-ID local (UUID tự sinh, persist) | mặc định |
| **Verified** | OAuth Google (+ Zalo VN) | 1-tap Google Sign-In |
| **Paid** | subscription | Stripe / VN gateway / coupon |

- Device-ID: `ANDROID_ID` hoặc UUID gen lần đầu, lưu encrypted prefs. Guest **không thu** info cá nhân.
- Mục tiêu Guest: đủ xài thử (chống meme "scam/lỏ") nhưng **không thể abuse quy mô lớn**.

## B. Quota matrix (tham số, tu chỉnh)
| | Guest | Verified | Paid |
|---|---|---|---|
| File tối đa | 100 MB | 10 GB | ∞ (200GB+ chunk) |
| Lần gửi / ngày | 5 | 50 | ∞ |
| Tổng bytes / ngày | 500 MB | 20 GB | ∞ |
| Send song song | 1 | 2 | 4+ |
| Expiry tối đa | 24h | 7 ngày | 30 ngày / no-expiry |
| Self-send / pairing | — | 1 thiết bị | nhiều |
| Resume / folder | — | — | ✓ |
| Priority speed | — | — | ✓ |

**Logic giá**: Guest đủ thử → Verified đủ tốt để đáng login (đòn abuse) → Paid đáng tiền (fund owned storage sau này). Chi phí biên ≈ 0 (TG chịu storage) nên margin cao.

## C. Kiến trúc gateway (chỗ gate ngồi)
Client **không bao giờ** nói chuyện trực tiếp MTProto. Mọi thứ qua **1 gateway nhỏ** của bạn:

```
Client ──(ciphertext)──▶ Gateway ──(MTProto)──▶ Telegram
                         │
                         ├─ auth + quota (Redis)
                         ├─ identity (DB tối giản)
                         └─ report/takedown
```

- `POST /send/begin` → auth (device token / OAuth / sub) + check quota + trả `send-session` token + target TG. Client **khai báo** (không tin) mime/size.
- `POST /send/chunk` → upload encrypted chunk → gateway proxy lên MTProto (gateway chỉ thấy ciphertext).
- `POST /send/commit` → ghi **encrypted manifest** (chunk-IDs + metadata) vào TG → trả **share-code** (chứa key).
- **Quota store**: Redis (counter per identity/device/day).
- **Identity store**: DB tối giản (`device_id, oauth_sub, tier, counters`). Mapping nhạy cảm (file→chunks) nằm trong **Telegram (encrypted)** — KHÔNG trong DB → không rủi_ro "world-open DB".

> Chỉ gateway giữ session MTProto → credentials Telegram không lộ cho client → khó abuse account trực tiếp.

## D. Login flow
- **Primary**: Google Sign-In (Credential Manager / One Tap) — free, universal trên Android, 1 tap.
- **Secondary**: Zalo OAuth — trust VN, đúng wedge localize.
- **OTP miễn phí** cho user có Telegram: gửi OTP qua chính account MTProto của bạn (vì bạn đã có TG). User không có TG → dùng Google/Zalo.
- **Bỏ SMS-gateway ban đầu** (tốn tiền) — thêm sau khi có revenue.
- Sau verify: gateway cấp JWT session, link device, mở Verified tier.

## E. Moderation dưới E2E (thật thà)
Vì E2E, gateway **không đọc được nội dung** → không scan CSAM/piracy ở server. Moderation =:
1. **Metadata/policy gate**: size, volume, rate, concurrency (server-enforced).
2. **Identity & reputation**: tuổi device-ID, pattern hành vi, block device/IP/OAuth đã lạm dụng.
3. **Client-declared mime/size** ở `begin` (không tin, chỉ coarse policy + analytics).
4. **Report → takedown**: receiver báo cáo share-code → gateway **xóa chunks theo manifest-ID** (không cần decrypt). Share chết ngay.
5. **Log metadata** (who/when/size/share-code/ciphertext-hash) cho abuse-response — KHÔNG log content.

**Risk ban giảm vì**: bạn là *encrypted pipe buồn ngủ* — low-volume + rate-limited + identity-gated → nhàm chán với abuse system của Telegram; và TG cũng không content-flag được ciphertext.

## F. Abuse playbook
- Quota vượt → `429` + nudge "login để tăng limit".
- Tín hiệu lạm dụng (nhiều report, pattern) → shadow-ban device + revoke OAuth + xóa chunks (takedown).
- Report flow 1 nút; SLA xóa nhanh.
- AUP/ToS rõ; hành động theo report; giữ audit metadata.

## G. Checklist MVP cho solo dev
1. Gen device-ID local + bảng tier.
2. Quota **server-enforced** (Redis counters) — không tin client.
3. Gateway endpoints `begin/chunk/commit`; chỉ gateway giữ MTProto.
4. Rate-limit (token bucket per identity/device/IP).
5. Report → takedown endpoint (xóa chunks theo manifest-ID).
6. Google Sign-In 1-tap → Verified.
7. Log metadata (không content) + ciphertext-hash.
8. ToS + Acceptable Use Policy.

**Bỏ qua (sau này)**: Zalo OAuth, OTP-via-TG, paid-tier integration, owned-storage migration (khi revenue đủ).

## Ghi chú trade-off
- E2E = không content-moderation được; đây là giá của claim privacy. Nói thật với user, đừng oversell.
- Quota là đòn cả hai việc (abuse + ban-risk) — tập trung làm tốt đòn này thay vì xoay account.
