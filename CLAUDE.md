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

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **quickshare** (1851 symbols, 4121 relationships, 136 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## When Debugging

1. `gitnexus_query({query: "<error or symptom>"})` — find execution flows related to the issue
2. `gitnexus_context({name: "<suspect function>"})` — see all callers, callees, and process participation
3. `READ gitnexus://repo/quickshare/process/{processName}` — trace the full execution flow step by step
4. For regressions: `gitnexus_detect_changes({scope: "compare", base_ref: "main"})` — see what your branch changed

## When Refactoring

- **Renaming**: MUST use `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` first. Review the preview — graph edits are safe, text_search edits need manual review. Then run with `dry_run: false`.
- **Extracting/Splitting**: MUST run `gitnexus_context({name: "target"})` to see all incoming/outgoing refs, then `gitnexus_impact({target: "target", direction: "upstream"})` to find all external callers before moving code.
- After any refactor: run `gitnexus_detect_changes({scope: "all"})` to verify only expected files changed.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Tools Quick Reference

| Tool | When to use | Command |
|------|-------------|---------|
| `query` | Find code by concept | `gitnexus_query({query: "auth validation"})` |
| `context` | 360-degree view of one symbol | `gitnexus_context({name: "validateUser"})` |
| `impact` | Blast radius before editing | `gitnexus_impact({target: "X", direction: "upstream"})` |
| `detect_changes` | Pre-commit scope check | `gitnexus_detect_changes({scope: "staged"})` |
| `rename` | Safe multi-file rename | `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` |
| `cypher` | Custom graph queries | `gitnexus_cypher({query: "MATCH ..."})` |

## Impact Risk Levels

| Depth | Meaning | Action |
|-------|---------|--------|
| d=1 | WILL BREAK — direct callers/importers | MUST update these |
| d=2 | LIKELY AFFECTED — indirect deps | Should test |
| d=3 | MAY NEED TESTING — transitive | Test if critical path |

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/quickshare/context` | Codebase overview, check index freshness |
| `gitnexus://repo/quickshare/clusters` | All functional areas |
| `gitnexus://repo/quickshare/processes` | All execution flows |
| `gitnexus://repo/quickshare/process/{name}` | Step-by-step execution trace |

## Self-Check Before Finishing

Before completing any code modification task, verify:
1. `gitnexus_impact` was run for all modified symbols
2. No HIGH/CRITICAL risk warnings were ignored
3. `gitnexus_detect_changes()` confirms changes match expected scope
4. All d=1 (WILL BREAK) dependents were updated

## Keeping the Index Fresh

After committing code changes, the GitNexus index becomes stale. Re-run analyze to update it:

```bash
npx gitnexus analyze
```

If the index previously included embeddings, preserve them by adding `--embeddings`:

```bash
npx gitnexus analyze --embeddings
```

To check whether embeddings exist, inspect `.gitnexus/meta.json` — the `stats.embeddings` field shows the count (0 means no embeddings). **Running analyze without `--embeddings` will delete any previously generated embeddings.**

> Claude Code users: A PostToolUse hook handles this automatically after `git commit` and `git merge`.

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
