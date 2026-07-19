# Trust without open source — TeleCloud

> Điểm yếu: phải chứng minh file mã hóa an toàn MÀ KHÔNG open source. Bài học từ app bị roast: nó oversell "bảo mật/ẩn danh" trong khi plaintext lên S3, mã chỉ là khóa tra cứu → bị bóc phốt công khai. TeleCloud phải chứng minh được, không tuyên bố.

## Reframe (quan trọng)
**Open source ≠ bằng chứng.** Binary user chạy khác source (cần reproducible build, mà gần như chẳng ai tự compile rồi cài). Đòn thật để chứng minh crypto đóng source:
**bằng-theo-cấu-trúc + bằng-theo-bắt-gói-tin + xác-nhận-độc-lập.**
Đúng cách 1Password (đóng), Signal, wormhole.app, magic-wormhole vẫn dùng.

## Stack chứng minh (xếp cost→uy tín)

### 1. Zero-knowledge by construction (free — lý lẽ mạnh nhất)
- Key nằm trong **URL fragment**: `telecloud.app/#<key>`. RFC 3986 §3.5: fragment **KHÔNG BAO GIỜ** gửi lên server.
- Server chỉ lưu **ciphertext** (manifest + chunks đều encrypt).
- → **Theo cấu trúc, server KHÔNG THỂ decrypt.** Không phải lời hứa — information-theoretic: không có key = AES-256-GCM bất khả phá.
- Tiền lệ: wormhole.app, magic-wormhole, Firefox Send, Bitwarden.

### 2. Network self-check (rẻ — "show don't tell")
- Toggle **"Security inspector"** trong app (hoặc hướng dẫn mitmproxy): log gói tin real-time.
- User **tự kiểm chứng**: mọi request body = ciphertext entropy cao (trông như random); **chuỗi key không bao giờ xuất hiện** trong bất kỳ request nào.
- Thuyết phục vì user tự làm, **không cần source**. Và build-specific → bắt được backdoor ngay trên đúng binary user đang chạy.

### 3. Spec crypto công khai + verifier nhỏ mở nguồn (vừa — uy tín chuyên gia)
- Spec: AES-256-GCM/chunk, nonce 96-bit random, AAD=`(manifest_id‖index)`, KDF argon2id, format manifest, **test vectors**.
- Phát hành **verifier ~150 dòng** mở nguồn (Python/JS): nhận ciphertext+key → decrypt+verify. Ai cũng đọc được.
- Logic: nếu verifier mở nguồn độc lập decrypt được file app tạo ra, **chỉ** từ key+ciphertext → crypto của app **CHÍNH LÀ** spec đó. Chứng minh format **không cần source app**.
- Ghép với #2 (app chạy thật emit đúng format đó) → chốt kín.

### 4. Crack-me / bug bounty (rẻ — bằng chứng xã hội)
- "Đây sample đã encrypt + spec; decrypt mà không có key → $5k." Đăng công khai HackerOne.
- Không ai bẻ được công khai = social proof + tạo attention.

### 5. Binary transparency (rẻ)
- Công bố **SHA-256 mỗi APK** + ký release (sigstore / Play App Signing). Phát hiện repackage/giả mạo.

### 6. Third-party audit (đắt — sau, khi có revenue)
- Công ty bảo mật có tên review source dưới NDA, chứng nhận claim, **công bố report**. Mô hình 1Password/WhatsApp/Signal. Roadmap, không phải MVP.

## Giới hạn trung thực
- Không chứng minh phủ định tới 100%. Nhưng #1+#2 = **tự kiểm chứng theo từng install**, mạnh hơn open source với đa số user (vì họ có bao giờ compile đâu).
- Bonus ăn khớp: ciphertext cũng = Telegram **không content-flag** được → **câu chuyện "trust" và "chống ban" củng cố lẫn nhau.** Một kiến trúc, hai lợi ích.

## MVP pick (no fallbacks — KHÔNG xếp lớp bằng chứng giả)
- **MVP: #1 (architecture) + #2 (network inspector) + #5 (signed builds).**
- **Ngay sau MVP: #3 (spec + verifier)** — đây là **viên gạch uy tín**.
- **#4/#6 khi có cộng đồng/revenue.**

## KHÔNG làm (tránh lặp sai app bị roast)
- Badge sáo rỗng ("military grade"), claim mơ hồ "AES-256" mà không có data-flow proof.
- Đừng hứa "sẽ open source" nếu không làm — nói THẬT cách chứng minh khi đóng nguồn.
