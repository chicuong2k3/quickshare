# US-CAP-11 — Tools landing pages

Source: `SPEC-quickshare-features` → CAP-11

**Story**
As a marketer/user, I want share-text, online-clipboard, and uncompressed-images landing pages, so that each tool use-case routes users into the send flow and grows adoption.

**Acceptance criteria**
- [ ] Each page renders its static shell instantly (HTMX lazy-load: no `HX-Request` → skeleton only, zero DB calls).
- [ ] Each page's CTA routes into the upload/send flow (`hx-get` + `hx-select` navigation, never plain anchors).
- [ ] Pages styled via prefixed global classes in `wwwroot/css/app.css` (HTMX partials lack component scope).
- [ ] Static-only content stays client-side — no HTMX round-trips for static data.

**Constraints in force**
- HTMX lazy-load default; `hx-select` on every partial request.
- No `hx-boost` globally; enhanced navigation for full-page links.
- Font sizes from DESIGN.md closed set; `--db-*` value-only overrides.

**Test anchors**
- Playwright: page shell renders <100 ms; CTA lands in send flow.
- `DownloadNoScriptTests`-style no-JS check for tool CTAs.

---

## Implementation Flow

1. **Shared shell** — all three tool pages use `ToolsLanding.razor` (`Components/Shared/ToolsLanding.razor`): params `Heading`, `Description`, `Lede`, `PasteHint`, `Body`; single CTA `href="@RoutePaths.Send"` (anchor, server-rendered — works with no JS).
2. **Share Text** — `/tools/share-text` → `ToolsShareText.razor`: copy EN, `Body` = share-text CTA; no custom script.
3. **Online Clipboard** — `/tools/online-clipboard` → `ToolsOnlineClipboard.razor`: `Body` = clipboard CTA; paste hint; relies on the global `paste-anywhere.js` (paste shortcut only, not required for first paint).
4. **Send Images Uncompressed** — `/tools/send-images-uncompressed` → `ToolsSendImagesUncompressed.razor`: `Body` = images CTA; `PasteHint` emphasized; same shell.
5. **No-JS guarantee** — every CTA is a plain `<a href>` to `/send` (HTMX lazy-load static-page exception applies — no DB/API calls on these pages), so `DownloadNoScriptTests`-style checks (no JS → CTA still navigates) pass without modification.
6. **i18n note** — tool copy is hardcoded EN/VI in markup (violates SharedLoc-only rule); documented as known debt — text lives in page markup, not resx.

## Code Structure

- `Components/Shared/ToolsLanding.razor` — shared landing shell + CTA.
- `Components/Pages/ToolsShareText.razor` / `ToolsOnlineClipboard.razor` / `ToolsSendImagesUncompressed.razor` — per-tool copy.
- `wwwroot/js/paste-anywhere.js` — optional paste enhancement (no-JS safe).
- `Infrastructure/Constants/RoutePaths.cs` — `RoutePaths.Send` anchor target.
