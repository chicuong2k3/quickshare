---
name: QuickShareCloud
description: A HyperCard stack, colorized — every delivery is a drawn white card on a putty dither desktop, with 2px ink borders, hard offset shadows, and six Apple colors rationed by role.

colors:
  sheet: "#EDE9DC"                 # putty desktop ground (light)
  sheet-raised: "#FFFFFF"          # the card
  sheet-deep: "#E2DDCE"            # recessed regions
  mountain: "#141414"              # every border is drawn ink
  mountain-deep: "#000000"
  valley: "#DDF1FB"                # blue wash — active/selected ground
  valley-ink: "#005F8C"            # blue ink — active text/focus
  foil: "#FDB827"                  # apple yellow — accent fill only
  foil-deep: "#F5821F"             # apple orange
  foil-ink: "#7A4A00"              # accent as type (7:1 on putty)
  ink: "#141414"
  ink-soft: "#55534E"
  stamp: "#141414"                 # the default button's ground (light)
  stamp-label: "#FFFFFF"
  lattice: "rgba(20,20,20,0.08)"   # the dither's ink component
  wash-green: "#E2F2DC"
  wash-blue: "#DDF1FB"
  wash-yellow: "#FBEED0"
  wash-orange: "#F9E4CF"
  disk: "#DDF1FB"                  # the drawn floppy's pale plastic
  error: "#C42B2F"
  success: "#2E8540"
  # Dark — the desktop at night. Same drawn cards, inverted ink.
  dark-sheet: "#1B1B1F"
  dark-sheet-raised: "#242429"
  dark-sheet-deep: "#141417"
  dark-mountain: "#E8E6DF"         # borders stay drawn — now in light ink
  dark-mountain-deep: "#FFFFFF"
  dark-valley: "#14384D"
  dark-valley-ink: "#6FC3EE"
  dark-foil-ink: "#FFCF66"         # accent as type on near-black
  dark-ink: "#F2F1ED"
  dark-ink-soft: "#A8A69F"
  dark-stamp: "#F2F1ED"            # the default button inverts at night
  dark-stamp-label: "#141414"
  dark-lattice: "rgba(242,241,237,0.06)"
  dark-wash-green: "#2F4A2B"
  dark-wash-blue: "#24455C"
  dark-wash-yellow: "#4A3D1E"
  dark-wash-orange: "#4A3320"
  dark-disk: "#9FD3EE"
  dark-error: "#FF6B5E"
  dark-success: "#6FCF87"
  # The six-band rainbow — progress and nothing else (top to bottom):
  rainbow-green: "#61BB46"
  rainbow-yellow: "#FDB827"
  rainbow-orange: "#F5821F"
  rainbow-red: "#E03A3E"
  rainbow-purple: "#963D97"
  rainbow-blue: "#009DDC"
typography:
  # The closed size set. Anything outside this list is a defect, not a choice.
  scale: ["0.5625rem", "0.625rem", "0.6875rem", "0.75rem", "0.8125rem", "0.875rem", "0.9375rem", "1rem", "1.125rem", "1.375rem", "2rem", "3.5rem"]
  display:
    fontFamily: "Bricolage Grotesque, Archivo, system-ui, sans-serif"
    fontSize: "clamp(2.1rem, 7vw, 4.25rem)"
    fontWeight: 800
    lineHeight: 1.0
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "Bricolage Grotesque, Archivo, system-ui, sans-serif"
    fontSize: "2rem"
    fontWeight: 800
    lineHeight: 1.05
    letterSpacing: "-0.015em"
  title:
    fontFamily: "Bricolage Grotesque, Archivo, system-ui, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "-0.01em"
  body:
    fontFamily: "Archivo, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.65
    letterSpacing: "normal"
  label:
    fontFamily: "Courier Prime, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.6875rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0.14em"
  titlebar:
    fontFamily: "Courier Prime, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.8125rem"
    fontWeight: 700
    lineHeight: 1.4
    letterSpacing: "0.08em"
  measurement:
    fontFamily: "Courier Prime, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0.02em"
    fontFeature: "tabular-nums"
rounded:
  none: "0px"        # cards, fields, modals, badges, everything drawn square
  button: "14px"     # the Mac rounded-rect button
  button-primary: "16px"
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
  # The default button: ink ground, paper label, outer double ring.
  button-primary:
    backgroundColor: "{colors.stamp}"
    textColor: "{colors.stamp-label}"
    rounded: "{rounded.button-primary}"
    padding: "7px 20px"
    ring: "0 0 0 3px {colors.sheet-raised}, 0 0 0 5px {colors.stamp}"
  button-primary-hover:
    backgroundColor: "{colors.mountain-deep}"
    textColor: "{colors.stamp-label}"
  button-primary-active:
    backgroundColor: "{colors.valley-ink}"
    textColor: "{colors.sheet-raised}"
  button-secondary:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: "7px 14px"
    border: "2px solid {colors.mountain}"
  button-secondary-hover:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.ink}"
  button-secondary-active:
    backgroundColor: "{colors.mountain}"
    textColor: "{colors.sheet-raised}"
  card:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "20px"
    border: "2px solid {colors.mountain}"
    shadow: "3px 3px 0 rgba(20,20,20,1)"
  field:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "8px 12px"
    border: "2px solid {colors.mountain}"
  field-focus:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    border: "2px solid {colors.valley-ink}"
  otp-cell:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    width: "2.75rem"
    height: "3.25rem"
  otp-cell-focus:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.ink}"
  titlebar:
    backgroundColor: "{colors.sheet-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "6px 10px"
  badge:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "3px 10px"
  nav-item:
    backgroundColor: "transparent"
    textColor: "{colors.ink-soft}"
    rounded: "{rounded.none}"
    padding: "6px 10px"
  nav-item-active:
    backgroundColor: "{colors.valley}"
    textColor: "{colors.ink}"
---

# Design System: QuickShareCloud

> Migration note: this specification describes the legacy DAUB interface. New and migrated
> components use vendored Unpoly.Blazor.Shadcn components and tokens; keep one design system per
> component while migration proceeds.

## Overview

**Creative North Star: "The HyperCard Stack, Colorized"**

Every delivery is a card in your stack; sending a file is flipping a card. The world is colorized classic-Mac / HyperCard grammar: a putty desktop covered in a 4px dither pattern, white cards with 2px drawn-ink borders and hard offset shadows (no blur, ever), striped title bars flanked by little boxes, and Mac rounded-rect buttons — of which exactly one per card wears the double outer ring that marked the button Return would press. The category's answer to "share a file" is a floating white card on a soft gradient; this system refuses that and refuses minimal flat-tech equally. Nothing floats — everything is *drawn*, with a visible line weight, on a visible desktop.

Color does specific jobs and no others. Six Apple colors are rationed by role: the blue wash is the active/selected state, the six-band rainbow is progress and nothing else, yellow is an accent fill that never becomes type on paper, and the green/blue/yellow/orange washes are solid state grounds behind pixel icons and step numbers. Everything else is ink on paper. Facts about a delivery — byte counts, share codes, hashes, expiry — are set in Courier Prime with tabular figures; the display voice is Bricolage Grotesque at 800, tight and lowercase, the confident hand-lettered title on the card.

The direction contract for this build lives as an HTML comment at the top of `<body>` in `src/QuickShare/App.razor`, seed **`ad3dafb6`** ("HyperCard Stack — user-pinned challenger, 'thêm nhiều màu'"). The approved comp and its approval record live in `.impeccable/mocks/hypercard-color/`; the world cards are `.impeccable/worlds/hypercard-*.webp`.

**Key Characteristics:**
- Putty dither desktop under every page: `repeating-conic-gradient` at 4px tile, ink component at 8% (light) / 6% (dark)
- White cards, 2px ink borders, hard offset shadows (2/3/5px, zero blur, full opacity)
- Striped title bar (`.qs-titlebar`): horizontal 1px stripes behind a mono label on its own paper patch, a drawn box at each end
- Cards and fields are square (radius 0 at token level); buttons are Mac rounded-rects (14px / 16px)
- The primary action is *the default button*: ink ground, paper label, outer double ring — inverted at night
- Pixel iconography: inline SVG rect grids with `shape-rendering: crispEdges` — no emoji, no unicode glyphs
- One authored motion: the HyperCard dissolve (`steps()` opacity), fired only when a delivery card appears
- Two mandatory themes: the putty desktop by day, the same desktop at night

**Vendor-override warning.** Every token name in this system is DAUB's (`--db-*`). DAUB is a third-party CSS framework loaded from `https://daub.dev/daub.css` in `App.razor`. The names are kept deliberately — ~322 uses of `db-*` classes across the markup re-skin for free — so **only the values are ours; never rename a token**. All values live in the two theme blocks at the top of `src/QuickShare/wwwroot/css/app.css`. The vendor's grunge family additionally uppercases headings and roughens card corners at `[data-theme^="grunge"]` specificity; `app.css` matches that specificity and undoes it. Legacy Miura-world names (`--db-sheet`, `--db-mountain`, `--db-valley`, `--db-foil`, `.miura-spec`, `.miura-rule`, `.miura-cell`, the `miura-deploy` keyframes) are kept as aliases into this world so nothing dangles — they are HyperCard values wearing old names.

## Colors

Ink on paper, six Apple colors rationed by role. Nothing is tinted for mood; every color is a state, a fill, or a band of the rainbow.

### Primary
- **Ink** (`{colors.ink}` light / `{colors.dark-ink}` dark): every border, every drawn line, primary text, and the default button's ground. The workhorse of the whole system — this world's authority is line weight, not hue.
- **Apple Yellow** (`{colors.foil}`): accent *fill* only — the highlight patch behind the hero's accent word, warning-banner grounds, the star glyph. It measures ~2.1:1 on paper and never carries type there. Same value in both themes.
- **Accent Ink** (`{colors.foil-ink}` light / `{colors.dark-foil-ink}` dark): the typographic form of the accent — `--db-color-accent-text`. Deep brown (7:1 on putty) by day, pale gold on near-black by night.

### Secondary
- **Valley Blue wash** (`{colors.valley}`) and **Valley Ink** (`{colors.valley-ink}`): the active/selected state. Active nav item, selected tab, focused OTP cell, drag-active dropzone, secondary-button hover, focus rings, text selection, the pressed state of the default button. Deepens at night (`{colors.dark-valley}` / `{colors.dark-valley-ink}`) but stays unmistakably blue.
- **The Rainbow** (`--db-rainbow`): a hard six-band vertical gradient — green `{colors.rainbow-green}`, yellow `{colors.rainbow-yellow}`, orange `{colors.rainbow-orange}`, red `{colors.rainbow-red}`, purple `{colors.rainbow-purple}`, blue `{colors.rainbow-blue}` — the classic-Apple stripe. It is the progress fill (`--db-progress-fill`, delivery download gauges) and the drawn floppy's label, and nothing else may wear it.

### Tertiary — the state washes
Solid per-theme grounds (`--db-wash-green/blue/yellow/orange`) behind pixel icons, step numbers, feature tabs, and the hero's ghost sheets. Solid, not translucent, so they survive the dark ground: `{colors.wash-green}` / `{colors.wash-blue}` / `{colors.wash-yellow}` / `{colors.wash-orange}` by day, `{colors.dark-wash-green}` etc. by night.

### Neutral
- **Putty** (`{colors.sheet}` / `{colors.dark-sheet}`): the desktop ground, always under the dither.
- **Card White** (`{colors.sheet-raised}` / `{colors.dark-sheet-raised}`): card, field, dropdown, modal, toast, navbar surfaces.
- **Putty Deep** (`{colors.sheet-deep}` / `{colors.dark-sheet-deep}`): recessed regions — progress tracks, muted grounds.
- **Ink Soft** (`{colors.ink-soft}` / `{colors.dark-ink-soft}`): muted spec-label text.
- **Disk Plastic** (`{colors.disk}` / `{colors.dark-disk}`): the drawn floppy's pale plastic; kept pale at night so the floppy still reads.

### Status
- **Error** (`{colors.error}` / `{colors.dark-error}`) and **Success** (`{colors.success}` / `{colors.dark-success}`): validation and terminal states. Text and borders, or a subtle fill — never a solid block of red or green.

### Dark Theme
The light desktop is `[data-theme="grunge-light"], [data-theme="light"]` and is the default (`App.razor` seeds `grunge-light`; `theme-color` is `#EDE9DC`). The dark desktop is `[data-theme="grunge-dark"], [data-theme="dark"]`. Dark is the same drawn world at night, not an inversion of roles: cards stay cards, borders stay drawn — now in light ink (`{colors.dark-mountain}`) — the hard drop goes to true black, the washes deepen, and **the default button inverts**: paper ground, ink label, paper ring. Each theme block carries the **full** token layer (palette, typography, geometry, shadows, focus, components); a block that redefines only raw colors half-applies and the page renders as an unreadable hybrid of ours and DAUB's defaults. Both themes are required; a surface that only works in one is not finished.

### Named Rules

**The Yellow-Is-A-Fill Rule.** Apple yellow is a fill and never a text color on paper, where it measures 2.1:1. Any accent read as type uses `--db-color-accent-text` (deep brown by day, pale gold by night). The hero's accent word is the canonical form: a yellow patch *behind* ink text (`background: var(--db-foil)` bled 4px by box-shadow), never yellow glyphs.

**The Rainbow-Is-Progress Rule.** The six-band rainbow (`--db-rainbow`) appears on progress fills and on the drawn floppy's label, and nowhere else. A decorative rainbow is a defect. A spent delivery's gauge greys its fill to muted text color — the rainbow is for progress still happening.

**The Blue-Means-Active Rule.** The valley wash is the open cell: active nav, selected tab, focused OTP, drag-over dropzone, hover on secondary buttons, focus outlines, `::selection`. Selection is never yellow and never rainbow.

**The Drawn-Ink Rule.** Every structural border is 2px solid ink (`--db-mountain`). One line weight, drawn, in both themes. 1px is reserved for hairline sub-rules inside a card (table rows, footer rule, gauge frames) — never for a card, button, or field edge.

## Typography

**Display Font:** Bricolage Grotesque (with `Archivo, system-ui, sans-serif`)
**UI/Body Font:** Archivo (with `system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`)
**Label/Mono Font:** Courier Prime (with `ui-monospace, SFMono-Regular, Menlo, monospace`)

All three are SIL OFL — a durable brand commitment in `PRODUCT.md` (2026-08-16, user-decided, license safety). They load from Google Fonts in `App.razor`: `Bricolage Grotesque:opsz,wght@12..96,400..800`, `Archivo:wght@400..800`, `Courier Prime:wght@400;700`. Outfit and Azeret Mono left with the Miura world; Playfair Display left before that. Do not reintroduce any of them, and no serif ever.

**Character:** Bricolage at 800, tight (-0.015em) and sentence-case, is the card's hand-painted title — warm and emphatic where the old world was cold and wide. Archivo is the plain-spoken UI voice. Courier Prime is the typewriter that stamps the card's facts: codes, sizes, hashes, expiry, title-bar labels — always uppercase, tracked, and tabular where digits appear.

### Hierarchy
- **Display** (800, `clamp(2.1rem, 7vw, 4.25rem)`, line-height 1.0, tracking -0.015em, `text-wrap: balance`): the home hero only. Emphasis inside it is the yellow patch behind the word (see Colors), never a color change of the glyphs.
- **Headline** (800, 2rem rising to 3.5rem at ≥640px, line-height 1.02–1.05, tracking -0.015em): page headings, `.db-h1/.db-h2`, `.miura-display`. `app.css` overrides both DAUB's base headings and the grunge family's uppercase at matching specificity.
- **Title** (700, 0.9375–1.125rem, tracking -0.01em): card titles, `.db-h3/.db-h4`.
- **Body** (400, 1rem, line-height 1.65): prose in `/tools` bodies, FAQ answers, hero subheads; 34–40rem measures.
- **Label** (400, 0.625–0.75rem, tracking 0.10–0.16em, uppercase, mono): `.miura-spec`, `.qs-readout`, badges, expiry chips, table headers, footer links, nav labels, the language select. Tracking relaxes to 0.1em below 560px.
- **Titlebar** (700, 0.8125rem, tracking 0.08em, uppercase, mono): `.qs-titlebar__label` — the card's name on its paper patch.
- **Measurement** (400, 0.8125rem, mono, `font-variant-numeric: tabular-nums`): file sizes, part counts, share URLs, hashes, countdowns.

### Named Rules

**The Real Measurement Rule.** The mono voice is reserved for facts the system actually knows: byte counts, part counts, expiry timestamps, share codes, hash prefixes, download counts, prices. Never a decorative serial, never "SPEC-001". If you cannot name the value's source, it does not go in mono.

**The Tabular Rule.** Any mono run containing digits carries `font-variant-numeric: tabular-nums`. Countdowns and gauges tick in columns and must not jitter.

**The Closed Scale Rule.** Font sizes come from the frontmatter's `typography.scale` set. A size outside that list is a defect, not a choice.

## Layout

The page is a single centered column (max 72rem), with narrower variants supplied by `PageWrapper`; content regions set their own measures — hero 46rem, features 56rem, comparison card 64rem, how-it-works 48rem, download card 30rem, upload container 44rem, `/tools` article 44rem, account card 28rem.

The spacing scale is compact and shared across every theme in one `[data-theme]` block: 4 / 8 / 10 / 14 / 20 / 24 / 32 / 40 / 64px. Buttons pad 7px × 14px with a 40px minimum height, rising to 44px below 767px so touch targets stay legal. Cards pad at 20px. The navbar is the menu bar: 3.25rem, card-white, a 2px ink rule beneath, `flex-wrap: nowrap`. The footer is a 1px ink hairline ending in the brand mark and a row of mono links. `html, body` carry `max-width: 100%; overflow-x: hidden`; wide content (the comparison table) scrolls inside its own container with `overflow-y: hidden` set explicitly to stop the -1px tab-margin trick from tripping a vertical scrollbar.

Breakpoints in use: **480px** (download card compaction), **560px** (hero padding and floppy shrink, second ghost sheet drops, brand lettering compresses, spec tracking relaxes), **640px** (upload headline jumps to 3.5rem, home steps stack, hero actions go vertical), **767px** (touch targets grow to 44px), **900px** (comparison table scrolls, why-badges stack).

### Named Rules

**The Dither-Ground Rule.** Every page sits on the desktop: `background-image: var(--db-dither)` (a `repeating-conic-gradient` of the lattice ink over transparent) at `4px 4px`, over the putty ground color. It is painted on `body` and `.layout-root`. Children re-use the same 4px dither as a texture fill only where a region should read as "desktop showing through" (the upload dropzone, the `/tools` box) — never at a different tile size.

**The Stack-Shows-Its-Edges Rule.** The stack is shown by offset ghost sheets: bordered card-shaped layers translated ~10–24px with sub-degree rotation, each wearing a state wash, behind the top card. This is the hero's thesis object; reuse it only where a literal stack of deliveries is being depicted.

## Elevation & Depth

**Depth is drawn, not blurred.** The card's shadow is the classic window drop: hard, offset, zero blur, full opacity — `2px 2px 0` (`--db-shadow-1`, delivery rows), `3px 3px 0` (`--db-shadow-2`, cards, dropdowns, toasts), `5px 5px 0` (`--db-shadow-3`, modals). Ink-colored by day, true black at night. `--db-shadow-inset` and `--db-shadow-ring` are `none`.

Buttons carry **no** drop shadow — a button is a drawn control, not a plate — and the button reset forces `box-shadow: none !important` on secondary/ghost. The single exception is the default button's ring, which is a box-shadow but reads as line work, not elevation. Hover never lifts anything: `--db-hover-scale`, `--db-active-scale`, `--db-hover-brightness` are all `1`, and the reset forces `transform: none !important`. Hover is a state change (deeper ground, blue wash), never a float.

### Shadow Vocabulary
- **`--db-shadow-1`** (`2px 2px 0` ink/black): delivery list rows.
- **`--db-shadow-2`** (`3px 3px 0` ink/black): cards, dropdowns, toasts — the workhorse.
- **`--db-shadow-3`** (`5px 5px 0` ink/black): modals only.
- **`--db-stamp-rule`** (`0 0 0 3px paper, 0 0 0 5px ink`): the default button's outer double ring; inverts with the theme.

### Named Rules

**The Hard-Drop Rule.** Every shadow in this world is offset with zero blur at full opacity. A soft shadow (any blur radius, any alpha below 1) is another world's material and is a defect here.

**The Nothing-Floats Rule.** Cards sit on the desktop with their drawn drop; they never gain shadow, scale, or translate on hover. Only overlay layers (dropdown, toast, modal) get the larger drops, and even those are hard.

## Shapes

**Cards are square; buttons are round.** At the token layer `--db-radius-0..5` and `--db-radius-full` are all `0px`, and card/field/modal/badge/toast/alert/dropdown radii follow — reinforced with `border-radius: 0 !important` on cards and inputs because the vendor sheet fights back. Against that squareness, buttons are Mac rounded-rects: `--db-btn-radius: 14px`, and the default button rounds slightly further to 16px so its outer ring keeps a parallel curve. This contrast — rectangular paper, lozenge controls — is the classic-Mac silhouette and the world's most identifiable geometry.

The old world's skew is retired: `--db-crease-angle` and `--db-crease-skew` are both `0deg`, and `--db-border-roughness` is `0px`. Nothing is skewed, torn, or clipped; everything is drawn orthogonally.

Iconography is **pixel drawing**: inline SVG built from a rect grid on a small viewBox (the hero's floppy is 24×24 rects: ink shell, disk-plastic body, shutter, rainbow label), rendered with `shape-rendering: crispEdges`. Icons sit in drawn square cells (2px ink border) on a state wash. No emoji, no unicode glyphs, no icon fonts, no smooth-path icon packs.

### Named Rules

**The Square-Card-Round-Button Rule.** Radius 0 on every surface, 14px/16px on every button. A rounded card or a square button breaks the silhouette; neither is a style option.

**The Crisp-Pixel Rule.** Every icon is an inline SVG rect grid with `shape-rendering: crispEdges`, drawn in the world's colors via `var(--db-*)` fills so it re-skins with the theme. If an icon cannot be drawn on a coarse pixel grid, simplify the icon.

## Components

### Buttons
- **Shape:** Mac rounded-rect, 14px radius (forced with `!important` over the vendor). 40px min height, 44px below 767px.
- **Typography:** Archivo 700, 0.875rem, sentence case, no tracking.
- **Primary — the default button:** ink ground (`--db-stamp`), paper label, no border, and the outer double ring `--db-stamp-rule` (3px paper, then 5px ink) that classic Mac dialogs drew around the button Return would press. 16px radius; horizontal padding widened by 6px per side. Hover deepens the ground to `--db-mountain-deep`; **press inverts to the active blue** (`--db-valley-ink` ground, paper label). At night the whole thing inverts: paper ground, ink label, paper ring. The ring is a box-shadow and costs no layout — keep at least 6px clearance around the button; focus-visible draws the valley-ink outline at 6px offset, outside the ring.
- **Secondary / Ghost:** card-white fill, 2px ink border, ink label. Hover opens the blue wash; active inverts fully to ink ground with paper label. There is no third variant.
- **Focus:** the global ring — `outline: 2px solid var(--db-valley-ink)` at 2px offset (6px on primary).

### Cards
- **Corner Style:** square. **Border:** 2px ink. **Shadow:** `--db-shadow-2`, unchanged on hover (hover may shift `border-color` only, 200ms).
- **Background:** card-white on the dithered desktop.
- **Internal Padding:** 20px.
- **Primitive:** `.miura-cell` in `app.css` (legacy name, current world): surface, 2px ink border, hard drop.

### The Striped Title Bar (signature)
`.qs-titlebar` — shared global title bar. Anatomy: a 13px drawn box, the mono uppercase label on its own paper patch, a second box; behind them, horizontal 1px ink stripes on a 4px rhythm (`repeating-linear-gradient` clipped to the content box), and a 2px ink rule below. It is the card's chrome — the thing that makes a card read as a window in the stack. It bleeds to the card's edges (zero the container's margin, as `.home-why__bar` does).

### Inputs / Fields
- **Style:** square (0 radius, forced), 2px ink border, card-white fill, 8px/12px padding. Any input holding a code, URL, or hash switches to Courier Prime with tracking.
- **Focus:** border shifts to valley-ink *and* the global 2px valley-ink outline appears. Both, not either.
- **Error:** mono at 0.6875rem in error red below the field.
- **Disabled:** `--db-disabled-opacity: 0.38`.
- **Select:** native appearance stripped; the chevron is a drawn SVG stroke in the world's line weight (a select without its drawn chevron stops reading as a control). Mono 0.6875rem uppercase, 2px ink border, square.

### OTP Cells (signature)
2.75rem × 3.25rem square drawn cells, 2px ink border, card-white fill, Courier Prime 700 at 1.125rem, one glyph per cell. On focus the cell opens the blue wash: valley fill, valley-ink border, valley-ink outline.

### Badges / Chips
Mono 700 at 0.6875rem, uppercase, square, 3px/10px padding. The home features' colored **card tabs** (`.home-feature-tab`) are the signature form: 2px ink border, a state wash fill, ink text — the stack's colored index tabs.

### Navigation
The navbar is the menu bar: card-white, 2px ink rule beneath. Items are Archivo 400 at 0.75rem, uppercase, tracked 0.14em, square, with a transparent 1px bottom hairline; hover inks that hairline; the active item takes the open cell — valley wash plus a valley-ink bottom border. Account section tabs (`.account-tabs`, globally shared) speak the same language with a `-1px` bottom margin merging their border with the strip's rule. The brand is a stacked lockup with a mono route-path line beneath it — wayfinding, not ornament.

### Notification center and file-request pagination (Unpoly migration)
The notification list keeps its valley-blue active filter tabs. "Load more notifications" and "Load more requests" links use the existing small ghost-button style; each is a real URL-backed link when JavaScript is off and becomes a reveal-triggered append with Unpoly. Requests update the next-page control without duplicating it. Direct entry renders the lists instead of permanent skeletons; no new color, type size or token is introduced.

### Progress / Gauge
A slim track (4–10px) framed in a 1px ink hairline over putty-deep; the fill is **the rainbow** (`--db-progress-fill: var(--db-rainbow)`), width animating over `--db-transition-slow` (460ms). A spent delivery greys its fill to muted text color and drops the row to 0.65 opacity.

### Dropzone
A patch of desktop inside the card: 2px *dashed* ink border (the one sanctioned dashed line — it means "put something here"), the 4px dither as fill over the surface color, generous 64px/24px padding. Hover inks the border to `--db-mountain-deep`; drag-active opens the valley — solid valley-ink border, blue wash fill, valley-ink icon cell.

### Diff Preview (`.dl-diff-*`)
The download text preview for `.patch`/`.diff` files (issue #248). The body switches to Courier Prime 0.8125rem with `tabular-nums`; each row is a flex line of old/new line numbers (muted, 2.5rem right-aligned), a marker column (1.25rem), and wrapping text. Add rows take the success wash, del rows the error wash — the state fills — with the marker (`+`/`-`) colored in the same hue so the change reads without color alone. Hunk rows are sheet-deep with accent text (the world's only bold mono); file-header rows (`--- `/`+++ `) are muted meta. The toolbar holds the mode/raw buttons at the small button size. Markup is JS-injected, so the styles live in `app.css` under the `dl-diff-*` prefix, not scoped CSS.

### The Dissolve (signature motion)
HyperCard's native transition, and the page's **only** authored motion: when a delivery card is created (`#upload-success`) or opened (`#download-phase-file`) it dissolves in — `opacity` stepped over `steps(6, end)` in 360ms, with the card's share-box / file-specs following one beat later (`steps(5, end)`, 260ms delay). Both elements start life `.hidden`, so the keyframe fires exactly when JS reveals them, never on an already-visible element. Never a slide, never a fold, never an ease. Everything else limits itself to the utility fades (`kira-fade-in/-up`, used for list staggers) and property transitions. `prefers-reduced-motion` kills all of it.

## Do's and Don'ts

### Do:
- **Do** define every new token inside the two theme blocks at the top of `app.css`, under a `--db-*` name, so custom CSS re-skins for free — and give it a value in **both** blocks.
- **Do** draw every structural edge as 2px solid `var(--db-mountain)`; reserve 1px for hairline sub-rules inside a card.
- **Do** use hard offset shadows only: `2/3/5px`, zero blur, full opacity, via the shadow tokens.
- **Do** keep cards and fields square and buttons at 14px (16px primary) radius.
- **Do** put exactly one default button (`<Button>`, Shadcn default variant) per card, with ≥6px clearance for its ring, and let press invert it to the active blue.
- **Do** put real measurements in Courier Prime with `tabular-nums`; give state a wash (`--db-wash-*`), activity the valley, progress the rainbow.
- **Do** draw icons as inline SVG rect grids with `shape-rendering: crispEdges`, filled with `var(--db-*)` so they re-skin per theme.
- **Do** check scoped styles when Unpoly swaps markup across component boundaries; use namespaced global styles when component scope does not reach the swapped markup.
- **Do** draw focus as `2px solid var(--db-valley-ink)` with 2px offset (6px on the primary, outside its ring), and honour `prefers-reduced-motion` by dropping all animation.
- **Do** keep the download page usable without JS: the server renders a working phase; JS only advances from it (`DownloadNoScriptTests` enforces this).

### Don't:
- **Don't** rename a `--db-*` token — the design token CSS custom properties are still used by custom `.razor.css` files and `app.css`. Change values only; do not remove or rename tokens.
- **Don't** put Apple yellow on text over paper (2.1:1); accent type is `--db-color-accent-text`.
- **Don't** use the rainbow anywhere but a progress fill (and the drawn floppy's label). Decoration never earns it.
- **Don't** blur a shadow, lift a card on hover, or scale anything. Depth is drawn.
- **Don't** round a card, field, modal, or badge — or square a button.
- **Don't** use emoji, unicode glyphs, icon fonts, or smooth-path icon packs; every icon is a crisp pixel rect grid.
- **Don't** author motion beyond the dissolve; and never attach the dissolve to an element that is visible at render.
- **Don't** reintroduce Outfit, Azeret Mono, Playfair Display, or any serif — the font set is Bricolage Grotesque / Archivo / Courier Prime, SIL OFL, by explicit user decision.
- **Don't** invent decorative serials or placeholder spec strings for the mono voice.
- **Don't** ship a surface tested in only one theme; the default button, washes, and disk color all change meaningfully at night.

## Honest Gaps

Recorded so the next contributor does not mistake intention for implementation.

- **The `/d/{code}` real file-card state was never rendered locally.** Downloading a real delivery needs R2 credentials the build did not have. The file card has only been verified in locked / expired / error states (`.impeccable/shots3/`).
- **Account pages, pricing/upgrade, and login were re-skinned through the tokens plus mechanical edits, not visually verified this round.** The token layer carries them, but nobody has eyeballed every state since the colorized world landed.
- **The striped title bar ships only on home** (hero card and why-card). Every other page's cards are plain 2px-ink cards without their chrome. Extending `.qs-titlebar` to the upload, download, and account cards is the most impactful unfinished move in the world.
- **A true 390px viewport was never captured** — the headless browser clamps its minimum width. The 480/560px rules are reasoned, not observed; check the default button's ring, the OTP row, and the hero stack on a real phone.
- **Five pre-existing `transition: width` warnings on progress fills** (`.account-usage-bar__fill`, `.account-delivery__gauge-fill`, `.p2p-receiver-progress-fill` and kin) predate this world and remain: width transitions trigger layout. Acceptable at these element sizes; migrate to `transform: scaleX()` if a gauge ever janks.
- **Vendor radii `--db-radius-sm/-md/-lg` are still not overridden** in the theme blocks; `.p2p-quota-banner`, `.ad-reward-card`, `.ad-reward-btn`, `.admin-flag-percent input` and `.tools-landing__kbd` reference them and therefore keep DAUB's rounding, violating the square-card law. Fix by zeroing those three aliases in both theme blocks.
- **Miura-era prose and line weights survive in scoped page CSS.** `Upload.razor.css` and `Download.razor.css` still open with folded-sheet narration, and several drawn cells there (download card frame, icon boxes, dropzone icon) still use 1px borders where the world's law is 2px. `Download.razor.css`'s `.btn` also draws the *old* inset stamp ring (`inset 0 0 0 6px/7px`) rather than the outer double ring — it renders acceptably because the stamp tokens now carry HyperCard values, but it is not the canonical default button.
- **Not canonized:** `.qs-zone` / `.qs-glyph` (dashed zone with an undefined `--db-transition-bounce` and a hover scale; circular glyph plate) and `.qs-eyebrow` are survivors from two worlds ago — inert where radius is zeroed, but not in this world's language; prefer the drawn cell, the wash square, and the titlebar label. The footer-hairline gap from the previous DESIGN.md is **resolved**: `MainLayout.razor.css` now rules the footer with `1px solid var(--db-mountain)` and no stale rgba values remain.
