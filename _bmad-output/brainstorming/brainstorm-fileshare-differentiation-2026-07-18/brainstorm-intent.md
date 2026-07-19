# Brainstorm Intent — Telegram-backed Zero-Knowledge File Zapper

> Kiến trúc ĐÃ XÁC NHẬN: backend **MTProto (userbot)**; file >2GB **chunk** rồi ghép lại khi download (kiểu TCP packet); **chưa mã hóa** client-side.
> Source canonical: `.memlog.md`. Drops vào `bmad-product-brief` / `bmad-prd`.

## Hero promise
> "TeleCloud file bao lớn cũng được, không cần tài khoản, free — và vì mã hóa trước khi upload, CHÍNH TELEGRAM CŨNG KHÔNG ĐỌC ĐƯỢC."

## Nước đi số 1 — E2E BEFORE upload (biến điểm yếu nhất thành selling point)
Hiện chưa mã hóa → Telegram (và ai có account/server TG) đọc hết. Fix:
- Sender: sinh AES-GCM 256-bit random → encrypt file → chunk **ciphertext** → upload lên Telegram.
- Key đi **out-of-band**: nhét vào share-code / URL fragment (`#`), KHÔNG bao giờ lên Telegram hay server. Code = key base32 (gõ tay được) HOẶC code → KDF (Argon2) → key.
- Mã hóa cả **manifest** (tên file, cấu trúc) — không thì Telegram thấy metadata.
- Receiver: nhập code → recover key → download ciphertext chunks → decrypt → reassemble.

## Chunking đúng chuẩn (TCP analogy + integrity)
- Mỗi chunk ≤ 2GB, upload từng document riêng, lưu list message-ID + thứ tự.
- **Manifest JSON**: thứ tự chunk, size, SHA-256 từng chunk + SHA-256 toàn file, tên gốc, mime (manifest cũng encrypt).
- Parallel upload/download (MTProto cho phép concurrent) → speed.
- Resume mức chunk khi rớt; verify hash khi reassemble.

## Khử mapping DB (dẹp rủi ro Firebase của app bị roast)
- Lưu manifest + danh sách chunk-ID thành **1 message Telegram đã encrypt**. Share-code mang key.
- → KHÔNG cần DB ngoài (Firebase/server) → không rủi ro "world-open DB". Telegram làm storage + DB luôn.

## Voi trong phòng — rủi ro ToS Telegram (existential)
- MTProto userbot lưu trữ file tự động lớn = đúng thứ Telegram **BAN** (ban account/channel theo đợt, đặc biệt khi scale).
- **Không xếp chồng fallback** (no-fallbacks). Chọn 1 backend và cam kết. Với MTProto: thiết kế ephemeral, rate-limit, tuân flood limits, **minh bạch với user rằng backend là Telegram và có thể bị gián đoạn** — KHÔNG hứa lưu forever, build expiry vào.

## Định vị (honest wedge)
WeTransfer (2GB + đọc file) · Telegram-trực tiếp (2GB + cần TG) · Wormhole free (cap relay) · LocalSend (cần app 2 bên, LAN only). Bạn: **vô hạn + free + không account + E2E thật + ephemeral.**

## Thang feature (lên 4 trục: riêng tư / dung lượng / tốc độ / ma sát)
- **Riêng tư**: E2E-before-upload · view-once / N-lượt / hết giờ · verify emoji anti-MITM (kiểu Signal) · bảng minh bạch trong app · reproducible build + audit.
- **Dung lượng**: chunking không giới hạn · parallel · resume · gửi folder.
- **Tốc độ**: concurrent chunks · live speed gauge · optional LAN P2P fast-path (cùng mạng).
- **Ít ma sát**: share-sheet 1 chạm · share-code gõ tay / QR · clipboard monitor · share-intent Zalo/TG/Messages.
- **Power user**: self-send (phone↔PC) · optional 6-digit device pairing · desktop companion.
- **Brand/VN**: lossless Quality badge · anti-MLM stance · localize VI thật (VI/EN/中文/한국어) · honest-freemium.

## MVP đề xuất (để vào bmad-prd)
E2E-before-upload + chunking có manifest/hash/resume + Telegram-as-encrypted-DB (không DB ngoài) + share-sheet 1 chạm + expiry. Đó là bản tối thiểu "honest & differentiable".

## Mở (deep-dive sau)
- Spec crypto chi tiết: KDF choice, AEAD mode, key encoding, manifest schema, key rotation.
- Liệu có cần server nào không (có thể chỉ URL shortener cho code).
- Plan cho ToS risk: rate-limit/flood strategy, ephemeral mặc định, messaging user.
