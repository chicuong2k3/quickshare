# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- **Senders** (primary): người gửi file lớn (ảnh gốc, tài liệu) — cá nhân và doanh nghiệp nhỏ Việt Nam, thao tác nhanh trên desktop và mobile, phân phối link qua Zalo/Facebook. Quan tâm: upload nhanh, link chia sẻ qua Zalo, không cần người nhận tạo tài khoản.
- **Recipients**: mở link trên bất kỳ thiết bị nào, không cần đăng ký; muốn file đúng, an toàn, tải nhanh.

## Product Purpose

QuickShareCloud gửi file kích thước bất kỳ (chunked, mã hóa đầu–cuối AES-256-GCM, khóa nằm trong URL fragment) qua link chia sẻ: upload-toàn-link trong vài phút, download có xác minh SHA-256. Recipient tải file mà không cần tài khoản.

## Positioning

- Faster and safer than email attachment (0 base unit): recipient không cần OAuth, chỉ cần link.
- Zero-knowledge: server không thấy plaintext — minh chứng bằng kỹ thuật (per-chunk AEAD, khóa trong fragment, AAD bind manifest).
- Plan-reinforced quota & tính năng (Free/Pro/Studio), đồng thời hỗ trợ upload ẩn danh cap 100 MB.

## Operation Context

- Upload/Download page: giao diện Blazor SSR + HTMX (shell render < 100ms, dữ liệu tải qua partial `hx-get`).
- Các route chính: home, `/send` upload, `/d/{code}` download, `/account`, `/account/profile`, `/account/billing`, `/pricing`, `/upgrade`, `/login`, `/p2p/send`, `/p2p/download` (webRTC), `/r/{code}` (FileRequest).
- Mobile-first demand: receiver often opens on phone; desktop has priority NFR-5 with 3 breakpoints.

## Capabilities and Constraints

- HTMX lazy-load là kiến trúc mặc định — shell render < 100 ms, data fill sau; layout `MainLayout` phải kế thừa `LayoutComponentBase`, HTMX partial dùng `hxmLayout`; **mỗi `hx-get` phải kèm `hx-select`** (không có Direct Routing trong .NET 10).
- Thread: 2 theme — light + dark, bắt buộc cả hai; mọi token phải có cặp giá trị.
- Token `--db-*` là **lớp đè lên vendor DAUB** (`https://daub.dev/daub.css`). Giữ nguyên tên token để ~322 lượt dùng class `db-*` trong markup không phải sửa. Đổi giá trị, không đổi tên.
- Hệ token grunge/kem cũ đã bị thay (2026-08-13). Chi tiết hệ hiện hành: `DESIGN.md`.

## Brand Commitments

- Tên: **QuickShareCloud**; tagline: "send files, not friction".
- Font (2026-08-16, user quyết, **chỉ font open source SIL OFL** để tránh rủi ro pháp lý): **Bricolage Grotesque** (display, 700–800, chữ thường, tracking âm nhẹ) + **Archivo** (UI/body, dễ đọc cho người dùng phổ thông) + **Courier Prime** (nhãn spec, mã, hash, dung lượng, số đếm, giá).
  Outfit + Azeret Mono **đã bỏ** cùng thế giới Miura (2026-08-16). Playfair Display đã bỏ từ 2026-08-13. Không đưa serif trở lại.
- Luật màu (thế giới HyperCard-màu): vàng Apple là **fill, không bao giờ là màu chữ trên nền giấy** — chữ accent lấy `--db-color-accent-text`. Wash xanh dương = trạng thái đang hoạt động/được chọn. Dải cầu vồng 6 màu = tiến độ, và chỉ tiến độ. Viền mực 2px = mọi thứ được "vẽ".
- Iconography: SVG inline **vẽ pixel** (lưới rect, `shape-rendering: crispEdges`) — không emoji, không glyph unicode thay icon.

## Existing Evidence

- **`DESIGN.md`** — hệ thị giác hiện hành (thế giới "HyperCard Stack, colorized", seed `ad3dafb6`), viết từ code đã dựng. Đây là nguồn chuẩn cho mọi trang mới. Comp đã duyệt + biên bản: `.impeccable/mocks/hypercard-color/`.
- UX/design token cũ: `_bmad-output/planning-artifacts/` (UEx + PRD + architecture spine) — là anti-reference cho redesign.
- Mã hiện tại: `src/QuickShare/` — toàn bộ giao diện.

## Product Principles

1. **Nhanh là tính năng** — shell render 0ms, lazy data, không blocking.
2. **Zero-knowledge có thể kiểm chứng** — mọi UI phải phản ánh thật (không over-promise).
3. **Recipient không bị cản** — không tài khoản, không rào.
4. **Có plan-bounnd, có quota** — mọi giới hạn hiển thị rõ.
5. **Mobile-first, desktop đầy đủ** — responsive 3 breakpoint.

## Accessibility & Inclusion

- NFR-6: keyboard navigation, focus ring, aria-live, prefers-reduced-motion — kế thừa và giữ.