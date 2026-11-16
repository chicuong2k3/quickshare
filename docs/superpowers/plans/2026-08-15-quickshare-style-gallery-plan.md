# QuickShareCloud Style Gallery Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one locally-openable HTML preview of an authored file-transfer workbench before production UI changes.

**Architecture:** Keep the preview isolated under `docs/ui-previews/`. A single HTML document contains semantic markup, a rule-and-column workbench layout, and a small vanilla JavaScript controller. File selection and submission only update visible local preview state.

**Tech Stack:** Standalone HTML5, CSS3 custom properties/media queries, vanilla JavaScript, inline SVG icons, no backend and no external dependency.

## Global Constraints

- Do not modify production Blazor pages, layouts, localization, tokens, upload JavaScript, or routing.
- Keep one stable upload task with no theme picker or color-skin variants.
- Use semantic HTML, native controls, visible status text, keyboard focus, reduced-motion support, and mobile-safe layout.
- Do not present fake upload completion, real delivery status, pricing, limits, encryption claims, or production functionality.
- Keep the document free of horizontal page scrolling at narrow widths.

---

### Task 1: Add the standalone workbench preview

**Files:**
- Create: `docs/ui-previews/quickshare-style-gallery.html`
- Test: manual browser inspection of the standalone file

**Interfaces:**
- Produces: a local HTML document with stable hooks `#send-workspace`, `#file-input`, `#recipient`, `#upload-status`, and `#send-status`.

- [ ] **Step 1: Add semantic page structure**

Create `header`, `main`, `nav`, `section`, `aside`, `form`, native `input[type=file]`, native email input, radio options, buttons, and text status regions. Use a split workbench: file table on the left, three-step delivery form on the right.

- [ ] **Step 2: Add workbench CSS**

Define cool paper, ink, rule, signal, and focus tokens. Use a two-column desktop composition with thin rules and a stacked layout below `760px`; avoid cards, gradients, pill selectors, and decorative shadows.

- [ ] **Step 3: Add local-only interactions**

Use one `DOMContentLoaded` handler. On file selection, render file names and sizes into the table and update `#upload-status`. On submit, prevent navigation and show `Preview only — no file was sent.` in `#send-status`. Never call `fetch`, `XMLHttpRequest`, or production routes.

- [ ] **Step 4: Add inline SVG iconography and prototype notice**

Use one consistent stroke style for upload, shield, and link icons. Add a visible notice stating that the page is a visual prototype and the buttons do not send files.

### Task 2: Verify the gallery and clean the deliverable

**Files:**
- Verify: `docs/ui-previews/quickshare-style-gallery.html`

**Interfaces:**
- Consumes: the standalone document from Task 1.
- Produces: a browser-checkable gallery with no production side effects.

- [ ] **Step 1: Validate the document structure**

Run a local static server from the repository root:

```powershell
python -m http.server 4173 -d docs/ui-previews
```

Open `http://127.0.0.1:4173/quickshare-style-gallery.html` and verify all six picker buttons render and each selection changes the preview without a reload.

- [ ] **Step 2: Verify interactions and accessibility**

Keyboard-tab through the picker, file input, email input, and send button. Select a sample file and submit the form; expect file metadata and the preview-only status text, with no navigation. Confirm every picker exposes `aria-selected` and every status region is readable text.

- [ ] **Step 3: Verify responsive behavior**

Check desktop width around `1440px` and mobile width around `390px`. Expect the upload form to remain visible, controls to remain tappable, and no horizontal scrollbar.

- [ ] **Step 4: Run the UI detector once**

```powershell
node .opencode/skills/impeccable/scripts/detect.mjs --json docs/ui-previews/quickshare-style-gallery.html
```

Fix only mechanical findings caused by the preview, then rerun the command once if fixes were needed.
