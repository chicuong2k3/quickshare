# QuickShareCloud — AI Rules

## Code Style

- **One statement per line** — không gộp nhiều câu lệnh trên cùng một dòng. Mỗi câu lệnh phải nằm trên dòng riêng của nó.
- **No magic strings** — không dùng hardcoded string/number literal trực tiếp. Phải dùng `const` hoặc `enum` (strongly typed).
- **Isolation CSS/JS** — luôn dùng scoped CSS (`.razor.css`) và JS isolation (`.razor.js`) cho mỗi component thay vì nhồi hết vào global `wwwroot/`.
