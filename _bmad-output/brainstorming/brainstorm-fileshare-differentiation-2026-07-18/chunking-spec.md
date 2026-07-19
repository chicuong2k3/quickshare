# Chunking Spec — TeleCloud (vượt trần 2GB Telegram)

> Phần **core của wedge thật**: file >2GB → chunk → upload Telegram (MTProto) → ghép lại khi download. Đi kèm E2E (encrypt trước chunk) như **khiên sống** (blob mã hóa không bị Telegram content-flag).

## Nguyên tắc
- Mỗi chunk là **1 document Telegram ≤ 2GB** (vượt đúng giới hạn cần lách). TeleCloud tự chọn chunk-size nhỏ hơn để song song/resume tốt.
- **Encrypt TRƯỚC khi chunk**: encrypt file → chunk ciphertext → upload. (E2E defensive, không headline "private".)
- **Mỗi chunk AEAD độc lập**: AES-256-GCM, nonce 96-bit random riêng mỗi chunk, AAD = `(manifest_id ‖ chunk_index)` → chống swap/reorder. Cho phép song song encrypt/decrypt + integrity từng chunk + resume sạch.
- **Manifest** (cũng encrypt) mô tả cấu trúc; **key chỉ nằm trong share-code / URL fragment**, KHÔNG trong manifest.

## Chunk size
- Default **512 MB/chunk** (file 20GB → 40 messages; 200GB → 400). Cân bằng giữa "ít message" và "resume/song song tốt".
- Tunable theo tier: Guest 256 MB (resume thô), Paid lên 1 GB (ít message, băng thông lớn).
- Giới hạn cứng: mỗi chunk **≤ 2 GB** (Telegram document cap).

## Manifest schema (JSON, encrypt với cùng key)
```json
{
  "v": 1,
  "name": "original.mkv",
  "mime": "video/x-matroska",
  "size": 21474836480,
  "sha256": "<toàn-file>",
  "chunk_size": 524288000,
  "chunks": [
    {"i":0,"size":524288000,"sha256":"<per-chunk>","msg_id":123,"file_ref":"..."}
  ],
  "crypto": {"alg":"AES-256-GCM","kdf":"argon2id","aad_scope":"manifest_id+index"}
}
```
- Manifest lưu thành **1 message Telegram**; `msg_id` manifest + key = share-code.
- KHÔNG chứa key. KHÔNG lộ plaintext (toàn bộ field nhạy cảm nằm trong ciphertext blob của chính manifest).

## Upload flow
1. Encrypt file (stream) → sinh nonce + AAD từng chunk.
2. Upload **N chunk song song** (concurrency theo tier: Guest 1–2, Verified 3–4, Paid 6+); mỗi chunk = 1 document → nhận `msg_id` + `file_ref`.
3. Tính SHA-256 từng chunk + toàn file.
4. Đủ chunk → **commit**: ghi manifest (encrypt) → nhận manifest `msg_id` → build share-code = `base32(key) + manifest_msg_id` (rút gọn qua short-link).
5. Chỉ tính "đã gửi" khi commit xong.

## Download / reassembly
1. Share-code → tách key + `manifest_msg_id`.
2. Lấy manifest (decrypt) → danh sách chunks.
3. Download **N chunk song song**; **verify SHA-256 từng chunk ngay khi về** (sai → re-fetch đúng chunk đó).
4. Ghép ciphertext → decrypt toàn file (hoặc decrypt từng chunk rồi ghép — vì AEAD độc lập).
5. Verify **SHA-256 toàn file**. Xong.

## Resume & reliability
- State per-chunk (`msg_id` đã có) → đứt mạng nối lại: chỉ upload/download chunk thiếu.
- `file_ref` MTProto hết hạn → refresh khi download (không phải lỗi).
- Chunk sai hash → re-fetch chunk đó, **không re-toàn-file**.
- Commit chỉ khi đủ → không bao giờ có "file nửa vời".

## Stream-as-you-go (nice-to-have)
- Receiver tải chunk 0 ngay khi sender upload xong chunk 0, dù chunk sau còn đang gửi → giảm tổng thời gian.

## Cleanup / expiry / takedown
- Hết hạn hoặc report → xóa toàn bộ chunk messages + manifest message (qua `msg_id`).
- Takedown theo `manifest_id`, **không cần decrypt** (chỉ cần xóa message).

## Demo chuyển đổi (tan câu "Telegram cap 2GB")
- File 8GB → Telegram chặn; TeleCloud chia 16×512MB, upload song song, receiver ghép lại + verify hash.
- → **Cho Guest thử 1 file >2GB miễn phí** để user cảm nhận "wow, TeleCloud đẩy 8GB qua Telegram được". Đó là khoảnh khắc convert.

## Open (sau)
- Lựa chọn thư viện MTProto (TDLib vs Telethon-server vs kotlin raw).
- Chiến lược private-channel layout (1 channel chung vs 1 topic/file).
- Đo benchmark chunk-size/concurrency thực tế trên mạng VN.
