# Style review — 11 hướng, **đã chọn #04 Bảng chuyến**

## Quyết định (11.11.2026)

Người dùng chọn **#04 Bảng chuyến** (split-flap board). Đây là *pin*, không phải roll —
hướng được pin luôn thắng roll. Roll chỉ dùng cho **bố cục bề mặt**:
`concept-seed.mjs --scope surface --mode operate` → seed `e98e0d52`, assignment
**candidate 6/7** (control-tower timeline) trên danh sách 7 cấu trúc tự rút:
1 hàng-tự-lật · 2 bảng-là-trang · 3 một-hàng-một-lúc · 4 hai-cột-lịch · 5 quầy-soát-vé ·
**6 trục-thời-gian-trung-tâm** · 7 mô-đun-như-máy-slot.

Bề mặt đã dựng (đều có bản 390px thật, xem `shot.mjs`):
- `worlds/04-send.html` — /send, board theo tệp (tên, dung lượng, tiến trình, trạng thái)
- `worlds/04-download.html` — /d/{code}: tệp → nút → đang tải → hạn
- `worlds/04-account.html` — /account: kỳ này (dung lượng), chuyến gần đây, cài đặt
- `worlds/04-pricing.html` — /pricing: bảng 3 cột trên desktop, 3 thẻ gói trên mobile

Số liệu ở /pricing và /account lấy từ code, không bịa: 15/200 GB/2 TB mỗi tháng, tệp tối đa
5/20/100 GB, link 7/30/90 ngày, 10/100/không giới hạn lượt tải, 1/3/10 ghế, thêm 5.000 đ/GB,
giá 50.000 đ và 200.000 đ mỗi tháng (`PlanCatalog`, `appsettings.json`).

## Luật hiển thị (sửa sau vòng duyệt đầu)

Người dùng chỉ thấy thứ họ cần để làm việc: **tên tệp, dung lượng, đang gửi/đã gửi, hạn link,
nút hành động, link để copy**. Không hiển thị — kể cả trong khối gập: số phần, khoảng byte,
tên thuật toán, vùng lưu trữ, hash, "kiểm tra SHA-256". Những thứ đó là chứng minh kỹ thuật, và chỗ
của chúng là trang `/security` hoặc tài liệu, không phải trang đang gửi/nhận tệp.

Bản đầu của #04 hiển thị cả bốn thứ đó ngay mặt tiền; bản hai đem gập lại. Cả hai đều sai — người
dùng không mở khối đó. Đã xoá hẳn. Ghi lại để lần sau không lặp.

## Đã dựng trong app (11.11)

- `src/QuickShare/Components/Pages/Pricing.razor` + `.razor.css` — trang thật theo thế giới bảng
  chuyến: bảng 3 cột (giá, tệp tối đa, hạn mức tháng, link sống, lượt tải, mật khẩu, hẹn giờ, no-ads),
  hàng nút dưới bảng, 3 thẻ gói cho điện thoại. Giá trị lấy từ `PlanTierLimits`/`PlanCatalog`.
  Chuyển tháng/năm giờ là **link thật** (`?billing=yearly`) — server quyết định, chạy được khi tắt JS;
  bản cũ dựa vào Alpine nên cả hai giá cùng hiện khi JS không tải được.
  Sửa luôn hai lỗi số liệu có từ trước: Studio hiện `2048 GB` (nay `2 TB`) và `2147483647` (nay `Unlimited`).
- `src/QuickShare/Components/Pages/AccountUsage.razor` + `.razor.css` — khối "kỳ này" trên `/account`
  thành bảng đọc: số dùng, một đường 1px có mốc, các dòng tệp lớn / lượt tải gần đây / quốc gia.
  Giữ nguyên `id="usage-card"` và toàn bộ truy vấn + quyền.

Đã kiểm: `dotnet build` sạch; 74 test E2E của pricing/account pass; chụp 1240px cho `/pricing`
tháng và năm. `/account` chưa chụp được vì dev cần magic link — khối này được kiểm bằng test
(`MonthlyQuotaTests`, `AccountUnpolyMigrationTests` đều assert `usage-card`).

Chưa làm: phần còn lại của hub `/account` (devices, referral, notifications…) vẫn là HyperCard;
pin vào `PRODUCT.md`; viết lại `DESIGN.md` từ bản đã dựng.

---

# Style review — 11 hướng giao diện mới

Bản duyệt cho QuickShareCloud, 11.11.2026. Không phải sản phẩm: đây là HTML tĩnh để so sánh
thế giới thị giác, không phải thứ được merge vào `src/QuickShare/`.

## Mở

```
.impeccable/mocks/style-review/index.html
```

Thẻ trên bảng duyệt là `iframe` tỉ lệ ~46% của đúng trang gốc; bấm **Mở →** để xem 1:1.
Cuối mỗi trang có nút **Sáng / tối** — cả 10 hướng đều khai hai theme, vì `PRODUCT.md` bắt buộc.

## Nội dung

| # | Hướng | Vật liệu gốc | File |
|---|-------|--------------|------|
| 01 | Bưu điện | giấy gửi phát, con dấu, mã vạch | `worlds/01-buu-dien.html` |
| 02 | Tem niêm phong | guilloche, microtext, serial | `worlds/02-tem-niem-phong.html` |
| 03 | Bảng phim | contact sheet, lỗ tiếp phim, safelight | `worlds/03-bang-phim.html` |
| 04 | Bảng chuyến | split-flap, cột trạng thái | `worlds/04-bang-chuyen.html` |
| 05 | Bảng đồng hồ | tấm kim loại, kim đo, con lăn số | `worlds/05-bang-dong-ho.html` |
| 06 | Lịch bloc | gáy lịch, đường xé, tuần | `worlds/06-lich-bloc.html` |
| 07 | Sổ tiết kiệm | giấy in liên tục, sao kê | `worlds/07-so-tiet-kiem.html` |
| 08 | Bản vẽ thi công | đường kích thước, khối tên, hatch | `worlds/08-ban-ve-thi-cong.html` |
| 09 | Kho kiện | kraft, stencil, phiếu gửi, mã vạch | `worlds/09-kho-kien.html` |
| 10 | Bàn trộn | dải kênh, fader, kim VU | `worlds/10-ban-tron.html` |
| 11 | Kính mờ | glass trên trường sáng (v2 vật liệu thật) | `worlds/11-kinh-mo.html` — bản cũ: `11-kinh-mo-v1.html` |

`shots/` giữ ảnh chụp 1180×830 của từng trang (tư liệu duyệt, không phải tài sản sản phẩm).

## Ràng buộc đã áp cho cả 10

- Font **SIL OFL** và phải có dấu tiếng Việt (Be Vietnam Pro / Archivo / Barlow / Courier Prime /
  Martian Mono / Allerta Stencil — bản dựng thật phải kiểm subset `vietnamese` của từng face).
- Hai theme bắt buộc, mỗi theme là một cảnh vật lý (quầy đêm, phòng tối, két, ca đêm…), không phải
  đảo màu máy móc.
- Tối đa một chuyển động tự tác giả mỗi thế giới (#04 lật flap, #03 khoanh bút chì, #05 kim/con lăn,
  #09 tia quét, #06 xé tờ), tất cả tôn trọng `prefers-reduced-motion` hoặc không tự chạy.
- Không emoji, không icon font. Với #01–#10 độ sâu là đường kẻ, vát, rãnh — không bóng mờ.
  #11 **là ngoại lệ có luật riêng**: bóng mờ và `backdrop-filter` chính là vật liệu, nên nó bị
  ràng buộc ngược lại — chỉ một nguồn sáng (trên-trái), viền sáng trên-trái / tối dưới-phải,
  không phát sáng loang, blur 16–18px, và luôn có hạt noise 3,5% để không thành kính máy sinh.
- Token phải map được sang Shadcn (`--background`, `--foreground`, `--primary`, `--border`,
  `--radius`) — mỗi hướng đã được dựng bằng biến CSS rời để chứng minh điều đó.

## Công cụ

`node shot.mjs <file> <out.png> <width> <height> [scale] [theme]` — chụp đúng viewport
(390px thật, không bị clamp như `--window-size`), có thể ép theme.

## Giới hạn của bản duyệt

- Dữ liệu là hư cấu: tên tệp, dung lượng, mã chia sẻ, số peer. Không có giá, không có benchmark,
  không có tuyên bố về tính năng chưa tồn tại.
- Ảnh trong #03 là gradient CSS, không phải ảnh thật; bản dựng thật cần thumbnail từ delivery.
- #11 dùng `backdrop-filter` (nặng trên Android yếu) và JS 1 nút để chứng minh trạng thái
  mờ→trong; bản dựng thật phải có fallback `@supports` và kiểm FPS trên máy tầm trung.
- Chưa có bản dựng 390px (khung headless clamp min-width) — cần kiểm trên máy thật trước khi chốt.
- Font tải từ Google Fonts CDN trong bản duyệt; bản dựng thật phải self-host (như hiện tại).

## Sau khi chọn

Chọn hướng **chưa** phải là chốt. Theo quy trình dựng thế giới: ghi hướng vào `PRODUCT.md` như một
brand commitment (đè HyperCard, luật icon pixel, và có thể cả bộ font — ba thứ đó do người dùng
quyết), chạy roll hướng, rồi viết lại `DESIGN.md` từ thứ đã dựng.
