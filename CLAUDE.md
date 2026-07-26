# QuickShareCloud — AI Rules

## HTMX Lazy Loading (DEFAULT — mọi page)

**Mọi page có `OnInitializedAsync` PHẢI dùng pattern này.** Nguyên tắc đơn giản:
- **Data tĩnh** (layout, heading, markup tĩnh) → trả ngay lập tức, không DB call
- **Data động** (DB query, API call) → fetch qua HTMX sau khi page load

Mục tiêu: page shell render tức thì (0ms), không block, data fill in async qua HTMX. Đây là kiến trúc mặc định, KHÔNG phải optional.

### Tại sao

| Cách | First paint | Trải nghiệm |
|---|---|---|
| **HTMX lazy-load** (rule này) | < 100ms — skeleton ngay | Shell hiện ngay, data fill sau |
| `[StreamRendering]` | 200–500ms — vẫn block async init | Page "nghĩ" 1 lúc, rồi stream |
| Plain `OnInitializedAsync` | 1–3s — block DB query | Page đơ, user thoát |

`[StreamRendering]` vẫn chạy `OnInitializedAsync` trong lần render đầu — nó chỉ giúp stream HTML SAU KHI async bắt đầu, chứ first byte vẫn delay. Nó là fallback, không phải giải pháp.

### Pattern

1. **Shell render**: Check `HX-Request` header. Không có → return ngay. Render skeleton.
2. **Data fetch**: Skeleton có `hx-get` + `hx-trigger="load"` + `hx-swap="outerHTML"`. Request thứ 2 có `HX-Request` → load data.
3. **Tất cả internal navigation**: Folder link, search, pagination, clear button — TẤT CẢ dùng `hx-get` target content area, KHÔNG dùng plain `<a href>` (gây full page reload).
4. **CSS cho HTMX partial**: Scoped CSS (`.razor.css`) KHÔNG áp dụng cho HTML từ HTMX partial của component khác. Dùng global styles trong `wwwroot/css/app.css` với prefix namespace (`account-`, `files-`, v.v.).

```csharp
protected override async Task OnInitializedAsync()
{
    var hxHeader = HttpContextAccessor.HttpContext?.Request.Headers["HX-Request"].FirstOrDefault();
    if (hxHeader != "true") return; // shell render — show skeleton, no DB calls

    // ... data loading (DB queries, Bus.InvokeAsync, ...) ...
}
```

```html
@* Shell skeleton — render tức thì *@
<div id="page-content"
     hx-get="/page-url"
     hx-trigger="load"
     hx-target="#page-content"
     hx-swap="outerHTML">
    @* skeleton markup *@
</div>
```

```html
@* Tất cả link navigation dùng HTMX *@
<a href="/page?filter=X"
   hx-get="/page?filter=X"
   hx-target="#page-content"
   hx-swap="outerHTML"
   hx-push-url="true">Filter X</a>
```

### Ngoại lệ

- **Page static** hoàn toàn (không async, không DB/API) không cần lazy-load.
- `[StreamRendering]` chỉ chấp nhận khi không thể tách shell + partial (interdependency phức tạp). Kể cả vậy, vẫn ưu tiên lazy-load.

## Code Style

- **One statement per line** — không gộp nhiều câu lệnh trên cùng một dòng. Mỗi câu lệnh phải nằm trên dòng riêng của nó.
- **No magic strings** — không dùng hardcoded string/number literal trực tiếp. Phải dùng `const` hoặc `enum` (strongly typed).
- **Isolation CSS/JS** — luôn dùng scoped CSS (`.razor.css`) và JS isolation (`.razor.js`) cho mỗi component thay vì nhồi hết vào global `wwwroot/`.
