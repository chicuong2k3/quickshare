# QuickShareCloud — AI Rules

## SSR and Unpoly migration

- Direct GET renders complete usable data, JavaScript disabled too; never gate on partial-request headers or emit permanent skeletons.
- Use native URL/form fallbacks; add Unpoly attributes for real server-rendered partial updates.
- Razor POST forms use unique `@formname` and `@onsubmit`; validate antiforgery and mutate only in the submit handler.
- Preserve authorization, URL/history, validation, CSRF, focus, and cache safety.
- Use opt-in Unpoly for partial updates and native forms as a fallback.
## Design System

- `DESIGN.md` describes legacy DAUB pages. New and migrated components use vendored Unpoly.Blazor.Shadcn components and tokens.
- Keep existing `--db-*` token names intact for unmigrated surfaces. Do not add new DAUB classes to a migrated component or mix both systems inside one component.
- Build generated Shadcn CSS from `src/QuickShare/tailwind.css` with the project's `css:build` script; do not edit output CSS.
- The download page must work with no JavaScript: server renders a usable phase, and JS only advances it. Keep `class="hidden"` off the default phase.
## Code Style

- **One statement per line** — không gộp nhiều câu lệnh trên cùng một dòng. Mỗi câu lệnh phải nằm trên dòng riêng của nó.
- **No magic strings** — không dùng hardcoded string/number literal trực tiếp. Phải dùng `const` hoặc `enum` (strongly typed).
- **CSS scoped, JS theo page** — CSS dùng scoped (`.razor.css`) cho mỗi component; dùng global `app.css` với prefix khi markup Unpoly vượt scope. JS riêng của page viết bằng TypeScript tại `src/QuickShare/ts/<page>.ts`, expose qua 1 object global (xem `download.ts`). **`wwwroot/js/**` là output do `tsc` sinh ra và bị `.gitignore` — file viết thẳng vào đó sẽ không có trong repo và biến mất ở lần clone kế tiếp.** Helper dùng chung cho các màn hình config nhỏ đã có sẵn ở `ts/page-actions.ts` (`window.qsPage`), nạp toàn cục từ `App.razor`.

## Cấu trúc dự án

```
src/QuickShare/
├── Program.cs                # composition root: DI, middleware pipeline, auth, Mediator, FastEndpoints
├── GlobalUsings.cs           # namespace dùng toàn cục (Data, Entities, Infrastructure.Constants, .Storage…)
├── Components/
│   ├── Layout/               # MainLayout, EmptyLayout
│   ├── Pages/                # Blazor SSR page (@page) — mỗi page: .razor + .razor.css
│   └── Shared/               # component tái sử dụng (AdComponent, PageWrapper…)
├── Data/
│   ├── AppDbContext.cs
│   ├── Entities/             # EF entities
│   └── Migrations/
├── Extensions/               # extension method (ClaimsPrincipal, IMediator, Result)
├── Features/<Tên>/<Action>/  # vertical slice — TOÀN BỘ code của 1 feature nằm ở đây
│   └── vd: Uploads/StartR2/ → StartR2Endpoint.cs, StartR2Handler.cs, StartR2Validator.cs
├── Infrastructure/           # CHỈ chứa thứ dùng chung cho ≥2 feature
│   ├── Constants/            # ApiTags, ErrorCodes, RoutePaths, PlanTierLimits… (no magic strings)
│   ├── Caching/              # CacheService (FusionCache)
│   ├── Jobs/                 # background job (cleanup, expiry, sweeper)
│   ├── Middleware/           # exception handler, rate limiting, request logging, SSR cache
│   ├── Storage/              # IStorageProvider + R2StorageProvider / LocalDiskStorageProvider
│   └── Errors.cs, QuickShareOptions.cs
└── wwwroot/                  # css/app.css (global), js/<page>.js, lib/
tests/QuickShare.Server.Tests/E2E/   # integration test qua TestWebAppFactory
```

## Recipe: thêm tính năng mới

Làm theo thứ tự con người sẽ làm:

1. **Đọc trước khi viết**: `Program.cs` (xem pipeline + DI đã có gì), 1 feature tương tự trong `Features/` để copy pattern — `Deliveries/Brand/` cho CRUD nhỏ, `Uploads/StartR2/` cho flow phức tạp — và entity liên quan trong `Data/Entities/`.
2. **Cần entity mới?** Thêm class vào `Data/Entities/`, thêm `DbSet` vào `AppDbContext`, chạy `dotnet ef migrations add <Tên>` (output vào `Data/Migrations/`).
3. **API endpoint**: tạo folder `Features/<Tên>/<Action>/` với tối đa 3 file: `<Action>Endpoint.cs` (FastEndpoints — auto-discover, KHÔNG cần đăng ký ở đâu), `<Action>Handler.cs` (Mediator handler nhận command/query record — cũng auto-discover), `<Action>Validator.cs` (FluentValidation, chỉ khi có input). Tag endpoint bằng const trong `ApiTags`.
4. **Hằng số**: route, error code, limit, header… vào `Infrastructure/Constants/` — không hardcode.
5. **UI**: page mới vào `Components/Pages/<Tên>.razor` + `.razor.css`, theo pattern SSR và Unpoly ở đầu file này và **hệ thị giác trong `DESIGN.md`** (xem mục Design System bên dưới). JS riêng của page vào `ts/<tên>.ts` (KHÔNG phải `wwwroot/js/` — đó là output bị gitignore) và expose qua 1 object global; khai báo object đó trong `ts/globals.d.ts`. Chuỗi hiển thị cho người dùng vào `SharedLoc.{en,vi}.resx`, **không hardcode** — chuỗi mà JS cần thì truyền qua một block JSON trong page (xem `download-i18n`).
6. **Service**: service chỉ 1 feature dùng → đặt ngay trong folder feature đó (vd `P2PQuotaService` nằm trong `Features/Shares/P2P/`). Chỉ khi ≥2 feature dùng chung mới đưa vào `Infrastructure/`. Đăng ký DI trong `Program.cs`.
7. **Test**: E2E phía server vào `tests/QuickShare.Server.Tests/E2E/`, dùng `TestWebAppFactory` (xem `DeliveryExtrasApiTests` làm mẫu seed data + client). Logic **chỉ tồn tại trong trình duyệt** (clipboard, sessionStorage, chuyển phase bằng JS) thì dùng `BrowserAppFixture` + Playwright (xem `PasteBrowserTests`) — nó chạy app như process thật và lái Chrome đã cài, không tải browser bundle.
8. **Xoá feature**: xoá folder `Features/<Tên>/` là endpoint + handler tự biến mất (auto-discovery), sau đó grep tên feature để dọn UI/JS/CSS/test còn tham chiếu.

## Giữ index GitNexus

Reindex bằng:

```bash
npx gitnexus analyze --skip-agents-md
```

`--skip-agents-md` giữ cho block bên dưới không bị ghi đè. Số đếm symbol/edge **không ổn định** — chạy lại cùng một commit đã cho 2324, 2349, 2372 — nên chúng đã được bỏ khỏi tài liệu: chúng gây churn git mỗi lần index mà không đổi cách ai đó viết code. (`--no-stats` có trong `--help` nhưng không có tác dụng.)

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **quickshare**. Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

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
