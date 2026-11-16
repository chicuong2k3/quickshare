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

- Upload/Download pages use Blazor static SSR. Direct GET renders usable authorized data; native URLs and forms work without JavaScript, with Unpoly enhancing server-rendered partial interactions.
- Các route chính: home, `/send` upload, `/d/{code}` download, `/account`, `/account/profile`, `/account/billing`, `/pricing`, `/upgrade`, `/login`, `/p2p/send`, `/p2p/download` (webRTC), `/r/{code}` (FileRequest).
- Mobile-first demand: receiver often opens on phone; desktop has priority NFR-5 with 3 breakpoints.

## Capabilities and Constraints

- Unpoly enhancement is opt-in for real server-rendered partial updates. Static SSR remains canonical, and migrated surfaces use Unpoly.Blazor.Shadcn components and theme tokens; legacy DAUB surfaces remain during phased migration.
- Thread: 2 theme — light + dark, bắt buộc cả hai; mọi token phải có cặp giá trị.
- Token `--db-*` là **lớp đè lên vendor DAUB** (`https://daub.dev/daub.css`) và chỉ còn giá trị cho các bề mặt **chưa migrate**. Giữ nguyên tên token để ~322 lượt dùng class `db-*` trong markup không phải sửa. Bề mặt mới dùng biến cục bộ của thế giới bảng chuyến, không thêm `db-*`.
- Trạng thái migrate: `/pricing` + khối usage `/account` đã theo `DESIGN.md`; phần còn lại chưa. Chi tiết + khoảng trống: `DESIGN.md`.

## Brand Commitments

- Tên: **QuickShareCloud**; tagline: "send files, not friction".
- Font (**chỉ font open source SIL OFL** để tránh rủi ro pháp lý, không serif): bề mặt bảng chuyến dùng **Barlow Condensed** (tên, nhãn — chữ hoa, giãn chữ) + **Martian Mono** (số, giá, dung lượng — `tabular-nums`); câu văn dùng **Archivo**. Bề mặt chưa migrate còn giữ **Bricolage Grotesque** + **Courier Prime** cho tới khi migrate xong.
  Outfit + Azeret Mono đã bỏ cùng thế giới Miura (2026-08-16). Playfair Display đã bỏ từ 2026-08-13.
- Luật màu (thế giới **bảng chuyến**, chốt 11.11.2026): **màu chỉ là trạng thái** — amber = hành động / đang chạy, xanh = xong / đã xác minh / không giới hạn, đỏ = bị giữ / thu hồi / hết hạn. Không tô màu để trang trí, không gradient, không bóng mờ. Đúng **một** nút amber mỗi màn.
- Hình khối: mọi bề mặt **vuông 0px**, độ sâu là **đường may 1px** (viền + đường kẻ trong lòng hàng), không radius, không blur, không đổ bóng.
- Iconography: **không icon, không emoji, không glyph unicode làm dấu trạng thái** — trạng thái là màu + chữ ("Đã xong", "Unlimited", "Sửa").

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
