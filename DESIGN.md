---
name: QuickShareCloud
description: A folded sheet you can measure — ciphertext as matte paper stock, creased at 60 degrees, with one gold tab that deploys it.
colors:
  sheet: "#EFEEEA"
  sheet-raised: "#F9F9F7"
  sheet-deep: "#E3E2DC"
  mountain: "#B9B6AE"
  mountain-deep: "#8E8B83"
  valley: "#CFE0F2"
  valley-ink: "#3F6187"
  foil: "#D4AF37"
  foil-deep: "#B8952A"
  foil-ink: "#6B5518"
  ink: "#0A0A0A"
  ink-soft: "#55524B"
  charcoal: "#2B2924"
  error: "#A4322A"
  success: "#4F7A5B"
  dark-sheet: "#16171A"
  dark-sheet-raised: "#1D1F23"
  dark-sheet-deep: "#101113"
  dark-mountain: "#3A3D43"
  dark-mountain-deep: "#575B62"
  dark-valley: "#24384F"
  dark-valley-ink: "#8FB6E0"
  dark-foil: "#E0BC4A"
  dark-foil-deep: "#C79F2E"
  dark-foil-ink: "#E8CB70"
  dark-ink: "#F2F1ED"
  dark-ink-soft: "#A5A49E"
typography:
  display:
    fontFamily: "Outfit, system-ui, sans-serif"
    fontSize: "clamp(2.1rem, 7vw, 4.25rem)"
    fontWeight: 200
    lineHeight: 1.0
    letterSpacing: "0.05em"
  headline:
    fontFamily: "Outfit, system-ui, sans-serif"
    fontSize: "2rem"
    fontWeight: 200
    lineHeight: 1.05
    letterSpacing: "0.1em"
  title:
    fontFamily: "Outfit, system-ui, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 300
    lineHeight: 1.25
    letterSpacing: "0.12em"
  body:
    fontFamily: "Outfit, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.65
    letterSpacing: "normal"
  label:
    fontFamily: "Azeret Mono, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.6875rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0.14em"
  measurement:
    fontFamily: "Azeret Mono, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0.02em"
    fontFeature: "tabular-nums"
rounded:
  none: "0px"
  full: "0px"
spacing:
  "1": "4px"
  "2": "8px"
  "3": "10px"
  "4": "14px"
  "5": "20px"
  "6": "24px"
  "7": "32px"
  "8": "40px"
  "9": "64px"
components:
  button-primary:
    backgroundColor: "{colors.foil}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "7px 26px"
  button-primary-hover:
    backgroundColor: "#E3C566"
    textColor: "{colors.ink}"
  button-primary-active:
    backgroundColor: "#9E7F1F"
    textColor: "{colors.ink}"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "7px 14px"
  button-secondary-hover:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.ink}"
  card:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "20px"
  field:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "8px 12px"
  field-focus:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
  otp-cell:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    width: "2.75rem"
    height: "3.25rem"
  otp-cell-focus:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.valley-ink}"
  badge:
    backgroundColor: "transparent"
    textColor: "{colors.charcoal}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "0.45rem 0.75rem"
  badge-active:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.valley-ink}"
  nav-item:
    backgroundColor: "transparent"
    textColor: "{colors.ink-soft}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "6px 10px"
  nav-item-active:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.ink}"
---

# Design System: QuickShareCloud

## Overview

**Creative North Star: "The Miura Sheet"**

Ciphertext is a folded sheet. The server holds a packet it cannot open; the key in the link is the one pull that deploys it. Every surface in this product is a region of one continuous piece of matte sheet stock, creased at 60 degrees, printed with the delivery's real measurements in monospace along hairline rules. The category's answer to "share a file" is a centered white card floating on a soft gradient ground — this system refuses that outright. There is no floating, no soft ground, and no rounded card. There are cells of a sheet, separated by creases.

The system's authority comes from measurement, not decoration. Byte counts, part counts, expiry timestamps, share codes, SHA-256 prefixes and prices are all set in Azeret Mono with tabular figures, and they are always true values pulled from the actual delivery. The display voice — Outfit at weight 200, uppercase, tracked wide — is engineering lettering, the register of a spec drawing, not a magazine headline. Colour does almost no work: everything is at rest in mountain gray until it becomes active (valley blue) or becomes the single thing you can pull (foil gold).

The system's discipline is in what it refuses. Zero radius everywhere, at token level. No shadows on cards, ever. No gradients across text. No decorative serial numbers in the mono voice. One gold element per screen at most. What is left is a sheet you can read the specs off.

**Key Characteristics:**
- Zero border-radius, enforced at the token layer — a crease has no radius
- 60-degree crease lattice as full-bleed page ground, fixed-attachment
- `skewX(-14deg)` parallelograms with counter-skewed glyphs for every icon cell
- Hairline (1px) rules that terminate in monospace spec labels
- Foil gold is a fill and never a text colour; deep bronze carries typographic accent
- Mono labels carry real measurements only — never decorative serials
- Two themes: light sheet and dark "packet in shadow"

**Verification note.** The direction contract for this build lives as an HTML comment at the top of `<body>` in `src/QuickShare/App.razor`, seed key **`46c7765a`**, form index 5 (bearer ticket). It is present and matches the built system except where noted under Honest Gaps below.

**Vendor-override warning.** Every token name in this system is DAUB's (`--db-*`). DAUB is a third-party CSS framework loaded from `https://daub.dev/daub.css` in `App.razor`. The names were kept deliberately so that the ~240 lines of `db-*` markup across 19 component files did not have to change; **only the values were replaced**, inside the two theme blocks at the top of `src/QuickShare/wwwroot/css/app.css`. When you add a token, add it to those blocks under a DAUB-shaped name. When you find a `--db-*` value you did not expect, check whether it is one of ours or one still coming from the vendor sheet — some are (see Honest Gaps).

## Colors

The palette is a paper palette: warm neutral stock, one cool crease shadow, one metallic. Nothing is saturated except the foil, and the foil is rationed.

### Primary
- **Foil Gold** (`{colors.foil}`): The single deployable action. Primary button fill, progress-bar fill, the recommended pricing cell's folded corner, the switch-on state. Never more than one per viewport. In dark theme it warms and lifts (`{colors.dark-foil}`) so it still reads as metal against near-black stock.
- **Deep Bronze** (`{colors.foil-ink}`): The typographic form of the accent. Any accent that is *text* — step numbers, the "recommended" badge, inline links — uses this, never the foil. On the light sheet it measures ~6.1:1; the foil itself measures ~2.1:1 and fails as text.

### Secondary
- **Valley Blue** (`{colors.valley}`) and **Valley Ink** (`{colors.valley-ink}`): The inside of a crease — the active/selected/open state. Active nav item background, selected tab, focused OTP cell, drag-active dropzone, "encrypted" fact chips, selected billing period, the underline crease beneath an emphasized headline word. Valley Ink is also the focus-ring colour and the selection highlight.

### Neutral
- **Sheet** (`{colors.sheet}`): The page ground. The crease lattice is drawn over it.
- **Sheet Raised** (`{colors.sheet-raised}`): Card, field, dropdown, modal and toast surfaces. A slightly whiter region of the same stock, not a floating plane.
- **Sheet Deep** (`{colors.sheet-deep}`): Recessed regions — progress-track backgrounds, pressed secondary buttons, disabled button fill, muted icon cells.
- **Mountain** (`{colors.mountain}`): Every hairline. Borders, rules, separators, table lines, cell divisions. This is the workhorse colour of the entire system.
- **Mountain Deep** (`{colors.mountain-deep}`): Hover-strengthened borders, scrollbar thumb, the brand's route-path label.
- **Ink** (`{colors.ink}`) / **Ink Soft** (`{colors.ink-soft}`) / **Charcoal** (`{colors.charcoal}`): Primary text, muted spec-label text, secondary body text.

### Status
- **Error Oxide** (`{colors.error}`) and **Field Green** (`{colors.success}`): terminal upload states and validation messages only, always as a 1px border plus an 8% subtle fill, never as a solid block.

### Dark Theme
The dark theme (`[data-theme="grunge-light"], [data-theme="dark"]`) is the same packet lit from one side, not an inversion. Stock goes to `{colors.dark-sheet}`; mountain hairlines become `{colors.dark-mountain}`; the valley darkens to `{colors.dark-valley}` while its ink brightens to `{colors.dark-valley-ink}` so the active cell still reads. Foil brightens; **and here the law flips** — in dark, `--db-color-accent-text` points at `{colors.dark-foil-ink}`, a pale gold that *is* legible as text on near-black. Both themes are required; a surface that only works in one is not finished.

### Named Rules

**The Foil-Is-A-Fill Rule.** Gold is a fill and never a text colour. It measures 2.1:1 on the light sheet. Any accent that must be read as type uses `--db-color-accent-text` (deep bronze on light, pale gold on dark). If you catch yourself writing `color: var(--db-foil)`, you have made a contrast bug.

**The One Pull Rule.** At most one foil element per viewport, and it must be the thing that actually deploys the packet — download, upload, upgrade. A selected toggle is not a pull; it takes valley. Pricing's billing-period switch explicitly overrides `db-btn--primary` back to valley for exactly this reason.

**The Three-State Rule.** Mountain gray is at rest. Valley blue is active or selected. Foil gold is deployable. There is no fourth state colour; if a component needs one, it needs a different structure instead.

## Typography

**Display Font:** Outfit (with `system-ui, sans-serif`)
**Body Font:** Outfit (with `system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`)
**Label/Mono Font:** Azeret Mono (with `ui-monospace, SFMono-Regular, Menlo, monospace`)

Both are loaded from Google Fonts in `App.razor` with `Outfit:wght@200..700` and `Azeret Mono:wght@300;400;500`.

**Character:** Outfit at 200–300 weight, uppercase and tracked to 0.05–0.22em, reads as engineering lettering off a technical drawing — light, wide, impersonal, confident. Azeret Mono is its measuring instrument: every fact about a delivery is set in it, tabular, small, tracked. The pairing has exactly two voices and no third; there is no serif in this system.

**Playfair Display was dropped by explicit user decision.** `PRODUCT.md` still lists it as a brand commitment; that line is stale. The code is the truth: Outfit carries display and UI, Azeret Mono carries measurement. Do not reintroduce a serif.

### Hierarchy
- **Display** (200, `clamp(2.1rem, 7vw, 4.25rem)`, line-height 1.0, tracking 0.05em, uppercase, `text-wrap: balance`): The home hero only. Emphasis inside it is a valley crease drawn as `box-shadow: inset 0 -0.22em 0 var(--db-valley)` under the word — never a colour change, never a gradient.
- **Headline** (200, 2rem rising to 3.5rem at ≥640px, tracking 0.06–0.1em, uppercase): Page-level headings and `.db-h1/.db-h2`. DAUB's own heading styles are overridden globally in `app.css` because the vendor sheet still carried a magazine register.
- **Title** (300, 0.9375–1.125rem, tracking 0.08–0.12em, uppercase): Card titles, `.db-h3/.db-h4`, dropzone titles, status headings, filenames. Filenames use `overflow-wrap: anywhere` rather than truncation — a long Vietnamese filename must stay readable on touch, where no hover reveals a title attribute.
- **Body** (400, 1rem, line-height 1.65): Prose in `/tools` landing bodies, FAQ answers, hero subheads. Constrained to 34–40rem measures.
- **Label** (400, 0.625–0.75rem, tracking 0.10–0.16em, uppercase, mono): `.miura-spec`, `.qs-eyebrow`, badges, expiry chips, table headers, footer links, the language select. Tracking relaxes to 0.1em below 560px so the labels stop wrapping.
- **Measurement** (300–400, 0.8125rem up to 2.25rem for prices, mono, `font-variant-numeric: tabular-nums`): file sizes, part counts, progress readouts, share URLs, hashes, prices. Prices are measurements and are set in mono at 2.25rem/300 with `letter-spacing: -0.02em`.

### Named Rules

**The Real Measurement Rule.** The mono voice is reserved for facts the system actually knows: byte counts, part counts, expiry timestamps, share codes, hash prefixes, download counts, prices. Never a decorative serial, never a fake ID, never "SPEC-001". If you cannot name the value's source, it does not go in mono.

**The Tabular Rule.** Any mono run containing digits carries `font-variant-numeric: tabular-nums`. Numbers in this system are read in columns and must not jitter as they tick.

**The No-Gradient-Text Rule.** Emphasis inside a headline is a valley crease under the word (`box-shadow: inset 0 -0.22em 0 var(--db-valley)`), never a gradient fill and never an accent colour on the glyphs.

## Layout

The page is a single centered column of maximum 72rem (`.page`), with narrower variants supplied by `PageWrapper`: `Centered` at 32rem, `Fill` at 42rem, `Account` at 32rem, `Padded` at full width with 24px block padding. Content regions inside a page set their own measures: hero 46–48rem, features 56rem, comparison table 64rem, download card 30rem, upload container 44rem, `/tools` article 44rem.

The spacing scale is compact and deliberately tightened over DAUB's defaults, re-declared for every theme in a shared `[data-theme]` block: 4 / 8 / 10 / 14 / 20 / 24 / 32 / 40 / 64px. Buttons are 7px × 14px padding with a 40px minimum height, rising to 44px below 767px so touch targets stay legal. Cards pad at 20px; the download card's body pads at 32/24/24, dropping to 24/14/20 below 480px.

Breakpoints in use: **480px** (download card compaction), **560px** (brand lettering compresses, primary button drops its clip-path, spec tracking relaxes), **640px** (share row goes horizontal, upload headline jumps to 3.5rem, pricing grid goes to 2 columns, home steps stack), **767px** (crease lattice tightens from 64px to 44px, touch targets grow), **900px** (comparison table scrolls, why-badges stack), **1024px** (pricing grid goes to 3 columns).

The navbar is 3.25rem tall, transparent, with a single mountain hairline underneath and `flex-wrap: nowrap`. The footer is a hairline rule that terminates in the brand mark and a row of mono links. `html, body` carry `max-width: 100%; overflow-x: hidden` — nothing in this system may set the document wider than the viewport, and any wide content (the comparison table) scrolls inside its own container.

### Named Rules

**The Crease-Ground Rule.** Every page sits on the lattice: two `repeating-linear-gradient`s at +60deg and −60deg, transparent for 63px then a 1px line at 10% text-colour opacity, plus the SVG grain, `background-attachment: fixed`. Below 767px the pitch tightens to 43px/44px and the grain drops. It is painted on `.layout-root` (and on `body` as a fallback) — never re-painted on a child except as a deliberate texture at a finer 47px/48px pitch (dropzone, `/tools` box).

**The Gap-Is-The-Crease Rule.** Adjacent cells share one hairline rather than each drawing their own. The pricing grid uses `gap: 0` with `outline: 1px solid` per card so the seam between two plans is a single crease, not a doubled border.

## Elevation & Depth

**This system is flat and has no card shadows.** `--db-card-shadow` and `--db-card-shadow-hover` are both `none`; `.db-card` is forced to `box-shadow: none !important` alongside `outline: none` and `border-radius: 0`. `--db-shadow-inset` and `--db-shadow-ring` are `none`. Hover on a card changes `border-color` only, over 200ms.

Depth is conveyed by three non-shadow means:
1. **Tonal layering on one stock** — sheet-deep is recessed, sheet is ground, sheet-raised is a surface. All three are the same paper.
2. **The folded corner** — a `linear-gradient` wedge of valley blue clipped into a cell's corner (16px on delivery rows, 18px on `.miura-cell` and pricing cells, 22px on the download card, 28px in foil on the recommended plan). This is the only "3D" gesture in the system.
3. **Material grain** — `--db-grain-sheet` (fractal noise, baseFrequency 0.9, 5% opacity, 140px tile) sits under the lattice as paper tooth. `--db-grain-foil` (anisotropic noise, baseFrequency `0.02 0.75`, 16% opacity, 120px tile) is layered under the primary button's fill so the gold reads as brushed metal, not as flat yellow.

### Shadow Vocabulary
Three shadow tokens exist but are used only where a surface genuinely detaches from the sheet:
- **`--db-shadow-2`** (`0 2px 8px rgba(10,10,10,0.10)`): dropdown and toast.
- **`--db-shadow-3`** (`0 6px 20px rgba(10,10,10,0.14)`): modal only.
- **`--db-shadow-1`** (`0 1px 2px rgba(10,10,10,0.06)`): declared, effectively unused.

### Named Rules

**The Nothing-Floats Rule.** Cards, buttons, inputs, badges and cells never carry a shadow, a transform lift, or a hover scale. `--db-hover-scale`, `--db-active-scale` and `--db-hover-brightness` are all `1`, and the button reset forces `transform: none !important`. Only overlay layers — dropdown, toast, modal — may leave the sheet.

## Shapes

**Radius is zero everywhere and it is enforced at the token level.** `--db-radius-0` through `--db-radius-5` and `--db-radius-full` are all `0px`, and `--db-btn-radius`, `--db-field-radius`, `--db-card-radius`, `--db-modal-radius`, `--db-toast-radius`, `--db-badge-radius`, `--db-alert-radius` and `--db-dropdown-radius` follow. Buttons, cards and inputs additionally force `border-radius: 0 !important` because the vendor sheet fights back. A crease has no radius.

The recurring silhouette is the **parallelogram**: `transform: skewX(-14deg)` on the container, with the glyph inside counter-skewed `transform: skewX(14deg)` so it stays upright. This is the shape of every icon cell (download state icons, file-type icon, home feature icons, home step numbers, dropzone icon, terminal-state icons), every OTP input cell, and the pricing feature marker (an 8px skewed square standing in for a stock circle-check). The skew angle is available as `--db-crease-skew` and the lattice angle as `--db-crease-angle: 60deg`.

The second silhouette is the **cut-corner tab**: `clip-path: polygon(12px 0, 100% 0, calc(100% - 12px) 100%, 0 100%)` on the primary button (14px on the download page's full-width variant), with horizontal padding increased by the same amount so the label clears the cuts. Below 560px the clip-path is dropped and the padding reverts — clip-path is not width-aware, and at narrow widths the cut edge starts eating the label and overhanging its container.

Borders are always exactly 1px, always mountain, never doubled. `--db-border-roughness` is `0px`; the sheet is cut, not torn.

### Named Rules

**The Counter-Skew Rule.** Any element carrying `skewX(-14deg)` must counter-skew its glyph child by `skewX(14deg)`. A skewed icon is a bug, not a style.

**The Clip-Path-Is-Not-Responsive Rule.** Any `clip-path` cut measured in px must be dropped, with its compensating padding, at the width where the cut would collide with content. The primary button does this at 560px.

## Components

### Buttons
- **Shape:** Square (0px radius, forced). Primary additionally carries the cut-corner tab clip-path.
- **Typography:** Outfit 500, 0.75rem, uppercase, tracking 0.09em.
- **Primary (the pull tab):** Foil fill layered as three backgrounds — the foil grain, a 102deg specular sweep (`rgba(255,255,255,0.34)` → transparent → `rgba(255,255,255,0.22)`), then flat foil — with ink-coloured label and a `foil-deep` 1px border. Padding 7px vertical, 14px + 12px clip compensation horizontal. Hover flattens to `#E3C566`; active to `#9E7F1F`.
- **Secondary / Ghost:** Transparent with a mountain hairline and text-coloured label. Hover fills valley and shifts the border to valley-ink; active fills sheet-deep. There is no third button variant.
- **Focus:** Non-clipped buttons take the global ring (`2px solid var(--db-valley-ink)`, 2px offset). The primary button sets `outline: none` and draws `box-shadow: inset 0 0 0 2px var(--db-valley-ink)` instead, because `clip-path` clips the outline away.
- **Sizes:** 40px minimum height (44px below 767px); `.db-btn--lg` at 44px minimum with 8px/18px padding; `.db-btn--icon` at 40px minimum width (44px below 767px).

### Cards / Cells
- **Corner Style:** Square, plus a valley-blue folded-corner wedge in one corner.
- **Background:** `{colors.sheet-raised}`, or transparent where the crease ground should read through (pricing cells).
- **Shadow Strategy:** None. See Elevation.
- **Border:** 1px mountain hairline. Adjacent cells share the seam.
- **Internal Padding:** 20px default; 32/24 on pricing cells; 32/24/24 on the download card.
- **Primitive:** `.miura-cell` in `app.css` is the canonical version — surface background, mountain border, and an 18px valley wedge at `top right`.

### Inputs / Fields
- **Style:** 1px mountain border, sheet-raised fill, 0 radius, 8px/12px padding. Any input that holds a code, URL, hash or measurement switches to Azeret Mono with 0.02–0.08em tracking.
- **Focus:** Border shifts to valley-ink *and* the global 2px valley-ink outline at 2px offset appears. Both, not either.
- **Error:** Message in mono at 0.6875rem, tracking 0.08em, in error oxide, 0.5rem below the field.
- **Disabled:** Sheet-deep fill, mountain border, muted text, `not-allowed` cursor. `--db-disabled-opacity` is 0.38.
- **Select:** Native appearance stripped; the chevron is drawn from two 5px `linear-gradient` triangles. Mono 0.6875rem uppercase, tracking 0.1em, transparent background, mountain border.

### OTP Cells (signature)
2.75rem × 3.25rem parallelograms (`skewX(-14deg)`), mountain border, surface fill, Azeret Mono at 1.125rem, centered. On focus the cell fills valley, its border goes valley-ink, and the 2px valley-ink outline appears. One glyph per cell, measured.

### Badges / Chips
Mono at 0.625rem, uppercase, tracking 0.14em, 1px mountain border, transparent fill, 0.45rem/0.75rem padding. The "encrypted" chip — a fact about the delivery, not a boast — takes the valley treatment (valley fill, valley-ink border and text). An interactive chip must carry a drawn chevron (`::after` with two 1px borders rotated 45deg) so it is never distinguished by colour alone.

### Navigation
Nav items are Outfit 400, 0.75rem, uppercase, tracking 0.14em, 6px/10px padding, with a transparent bottom hairline. Hover fills that hairline with mountain-deep; the active item takes the valley cell — valley background plus a valley-ink bottom border. Account section tabs (`.account-tabs`, global because they ship inside HTMX partials) use the identical language at 0.75rem/0.12em with a `-1px` bottom margin so their border merges with the tab strip's rule.

The brand is a stacked lockup: `QUICKSHARE` in Outfit 300 at 0.16em tracking, `CLOUD` in 200 muted beneath it, and a 0.5625rem route-path line at 0.2em tracking in mountain-deep showing the route the visitor is actually on — wayfinding, not ornament. Below 560px the `CLOUD` line is dropped and tracking compresses.

### The Hairline Rule (signature)
`.miura-rule` is a flex row whose `::before` is a 1px mountain line at `flex: 1`, so any label placed in it is pushed to the end of a rule that spans the remaining width. The centered variant — used on the home hero's spec line, the upload subhead and the pricing lede — flanks the label with two 6rem-max rules via `::before` and `::after`. This is the world's most recognizable device; use it whenever a small mono fact needs to sit on the sheet.

### Progress / Gauge
A 6–8px track with a mountain border and sheet-deep fill; the fill is flat foil and animates its width over `--db-transition-slow` (460ms). A spent delivery greys its fill to muted text colour and drops the whole row to 0.65 opacity.

### Dropzone
A cell of the sheet at a finer crease pitch: `repeating-linear-gradient(60deg, transparent 0 47px, rgba(text,0.06) 47px 48px)` over the surface colour, 1px mountain border, 64px/24px padding. Hover strengthens the border to mountain-deep. Drag-active opens the valley — the fill becomes valley blue, the lattice line becomes `rgba(63,97,135,0.14)`, and the skewed icon cell takes the valley-ink border and colour.

## Do's and Don'ts

### Do:
- **Do** define every new token inside the two theme blocks at the top of `app.css`, under a DAUB-shaped `--db-*` name, so existing `db-*` markup re-skins for free.
- **Do** give every new surface both themes. Test `[data-theme="grunge-dark"]` (the light sheet) and `[data-theme="dark"]` (the packet in shadow) before calling it done. The naming is inverted from the vendor's; trust the values, not the name.
- **Do** put real measurements in mono — byte counts, part counts, expiry, codes, hashes, prices — with `font-variant-numeric: tabular-nums`.
- **Do** use `--db-color-accent-text` for any accent that is type, and `--db-foil` only as a fill.
- **Do** counter-skew every glyph inside a `skewX(-14deg)` container by `skewX(14deg)`.
- **Do** style cross-component HTMX partial markup from `app.css` with a namespaced global prefix (`account-`, `tools-landing__`, `ad-`, `p2p-`). Scoped `.razor.css` does not reach HTML rendered by another component's partial — this is a project-wide constraint, not a shortcut.
- **Do** draw focus as `2px solid var(--db-valley-ink)` with `outline-offset: 2px`. Where `clip-path` is present, draw it as `inset 0 0 0 2px var(--db-valley-ink)` instead, because clip-path eats outlines.
- **Do** honour `prefers-reduced-motion` by dropping transitions only. The `-14deg` skew is geometry, not motion, and stays.
- **Do** let wide content scroll inside its own container; the document body must never scroll horizontally.

### Don't:
- **Don't** introduce a border-radius anywhere. Not on a chip, not on an avatar, not on a modal.
- **Don't** put foil gold on text. It measures 2.1:1 on the light sheet.
- **Don't** put two foil elements in one viewport, and don't make a selected/toggled state gold — selection is valley.
- **Don't** add a box-shadow or a hover lift to a card, button, input or cell. Only dropdown, toast and modal leave the sheet.
- **Don't** reintroduce Playfair Display or any serif. `PRODUCT.md`'s font line is stale; the code is the truth.
- **Don't** invent decorative serials, fake IDs, or placeholder spec strings for the mono voice.
- **Don't** use a gradient across type. Emphasis is a valley crease under the word.
- **Don't** measure a `clip-path` in px without also dropping it at the narrow breakpoint where the cut would eat the label.
- **Don't** re-paint the crease lattice at the page ground's 63/64px pitch inside a child element; use the finer 47/48px texture pitch instead, so the two never moiré.

## Honest Gaps

Recorded so the next contributor does not mistake intention for implementation.

- **The world does not deploy.** The Miura sheet is named for the motion of unfolding, and **nothing on any page unfolds.** There is no deployment animation anywhere in the system. Motion is limited to `kira-fade-in` / `kira-fade-up` (opacity plus a 12px rise, 0.4–0.5s, with a 6-step stagger), width transitions on gauges, colour transitions on borders and fills, and the FAQ's `max-height` reveal. The folded corner is a static gradient wedge, not a fold that opens. If the world is ever to earn its name, this is the missing piece.
- **The `/d/{code}` file-card state was never rendered locally.** Downloading a real delivery requires R2 credentials that were not available during the build. The `.file-row`, `.file-specs`, `.badges` and `.zk-panel` styling in `Download.razor.css` is written but has only been seen in its locked / expired / error states. Verify it against a real delivery before trusting the spacing.
- **A true 390px viewport was never captured.** The 480px and 560px rules are reasoned, not observed. Check the primary button, the OTP row and the pricing table on a real phone.
- **Some radii still come from the vendor.** The override block zeroes `--db-radius-0..5` and `--db-radius-full`, but **not** `--db-radius-sm`, `--db-radius-md` or `--db-radius-lg`. Those names are still referenced by `.p2p-quota-banner`, `.ad-reward-card`, `.ad-reward-btn` and `.tools-landing__kbd`, which therefore keep DAUB's rounding and violate the zero-radius law. Fix by zeroing those three aliases in the theme block.
- **`.qs-zone` and `.qs-glyph` are pre-Miura survivors.** They carry a 2px dashed border, `--db-radius-4`, `--db-radius-full` and a `1.006` hover scale, and `.qs-zone` references `--db-transition-bounce`, which the theme block never defines. They are inert where radius is zeroed, but they are not written in this world's language. Prefer `.miura-cell` and the skewed icon cell.
- **The secondary button is unfinished by design.** The board's secondary was to carry a trailing valley cell with an arrow in it; the arrow needs per-button markup that pass could not add, and the cell alone reads as an unexplained sliver. It ships as a plain hairline cell until the arrow can ship with it. This is documented in `app.css` at the rule itself.
- **`.db-navbar` and `.layout-footer` in `app.css` still carry `rgba(28,22,16,0.06)` hairlines** — a leftover from the previous world's palette that does not track either theme. `MainLayout.razor.css` overrides the navbar correctly with `--db-mountain`; the footer's global rule does not.
