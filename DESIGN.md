---
name: QuickShareCloud
description: A departure board — charcoal instrument panels, flap rows with mechanical seams, colour only ever as status. Plans, deliveries and usage are read the way a station reads its timetable.
colors:
  board: "#15181B"          # page instrument ground (dark)
  panel: "#1D2125"          # panel behind a board
  flap: "#2A2F35"           # a flap row's module
  flap-deep: "#31373E"      # seam between modules
  slot: "#0E1113"           # empty cell, readout window
  ink: "#E9ECEE"            # every label and figure
  ink-dim: "#8B939B"        # secondary label, unit
  hair: "#31373E"           # the only border weight in this world
  amber: "#E0A03C"          # action + in-flight
  green: "#4FA36A"          # done, verified, unlimited
  red: "#C85A4F"            # held, revoked, expired
  board-light: "#D3D6D9"
  panel-light: "#E2E5E7"
  flap-light: "#CFD3D6"
  flap-deep-light: "#C3C7CB"
  slot-light: "#F1F3F4"
  ink-light: "#171A1D"
  ink-dim-light: "#5C6468"
  hair-light: "#B3B8BC"
  amber-light: "#A96C10"
  green-light: "#2F7A46"
  red-light: "#A83B31"
typography:
  scale: ["0.5rem", "0.55rem", "0.6rem", "0.65rem", "0.72rem", "0.78rem", "0.85rem", "0.95rem", "1.1rem", "1.15rem", "1.75rem", "3rem"]
  display:
    fontFamily: "Barlow Condensed, Archivo, system-ui, sans-serif"
    fontSize: "clamp(1.75rem, 4vw, 3rem)"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "-0.005em"
    textTransform: "uppercase"
  headline:
    fontFamily: "Barlow Condensed, Archivo, system-ui, sans-serif"
    fontSize: "1.1rem"
    fontWeight: 600
    lineHeight: 1.1
    letterSpacing: "0.12em"
    textTransform: "uppercase"
  label:
    fontFamily: "Martian Mono, ui-monospace, monospace"
    fontSize: "0.5rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0.14em"
    textTransform: "uppercase"
  data:
    fontFamily: "Martian Mono, ui-monospace, monospace"
    fontSize: "0.65rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "0.02em"
    fontFeature: "tabular-nums"
  body:
    fontFamily: "Archivo, system-ui, -apple-system, 'Segoe UI', sans-serif"
    fontSize: "0.85rem"
    fontWeight: 400
    lineHeight: 1.55
    letterSpacing: "normal"
rounded:
  none: "0px"               # every surface, control and cell in this world
spacing:
  "1": "4px"
  "2": "8px"
  "3": "10px"
  "4": "12px"
  "5": "14px"
  "6": "20px"
  "7": "24px"
  "8": "32px"
components:
  board:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    border: "1px solid {colors.hair}"
  board-head:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink}"
    borderBottom: "1px solid {colors.hair}"
  row:
    backgroundColor: "{colors.flap}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    borderTop: "1px solid {colors.flap-deep}"
    shadow: "inset 0 -1px 0 rgba(0,0,0,0.45)"
  cell-empty:
    backgroundColor: "{colors.slot}"
    borderTop: "1px solid {colors.flap-deep}"
  status-done:
    textColor: "{colors.green}"
  status-flight:
    textColor: "{colors.amber}"
  status-blocked:
    textColor: "{colors.red}"
  action-primary:
    backgroundColor: "{colors.amber}"
    textColor: "#16181A"
    rounded: "{rounded.none}"
    padding: "10px 20px"
  action-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.ink-dim}"
    rounded: "{rounded.none}"
    border: "1px solid {colors.hair}"
  meter:
    backgroundColor: "{colors.slot}"
    fillColor: "{colors.green}"
    markerColor: "{colors.amber}"
    height: "6px"
---

# Design System: QuickShareCloud

> **Migration note (11.11.2026).** The world below is pinned. It is built on `/pricing`, `/send` and
> the `/account` usage block; every other surface still carries the legacy HyperCard world over DAUB
> `--db-*` tokens. Migrate one surface at a time and do not mix the two languages inside one
> component. `DESIGN.md` describes the target; the "Honest Gaps" section lists what is still legacy.

## Overview

**Creative North Star: "The Departure Board."**

The product's job is to tell you what is happening to a delivery right now and what it will cost.
That is what a station board does, and the whole system is built from that grammar: a charcoal
instrument panel, rows of flap modules seam-stacked one above another, figures set in mono with
tabular numerals so they do not jitter, and one line of condensed capitals for the name of the thing.
Nothing floats, nothing glows, nothing is rounded. Depth is a **seam**, not a shadow.

Colour is a status channel, never decoration. Amber means *act / in flight*. Green means *done,
verified, unlimited*. Red means *held, revoked, expired*. Everything else is ink on panel. There is
one accent per view because there is one decision per view.

The competitive answer in this category is a soft gradient hero with three rounded price cards and a
purple "most popular" ribbon. This system refuses it: plans are **columns on a timetable**, facts are
**rows**, and the recommended column is marked by a single amber rule and a small tracked tag — not by
a thicker border, a lift, or a colour wash.

**Key Characteristics:**
- Charcoal board (`#15181B`) with panels one step lighter (`#1D2125`) and flap rows one step lighter again (`#2A2F35`)
- Hairline seams: 1px `#31373E` between everything, plus an inset 1px dark line under each row
- Zero radius everywhere — panels, buttons, cells, tags
- Status is colour and position, never an icon
- Figures in Martian Mono with `tabular-nums`; names in Barlow Condensed capitals, tracked
- One authored motion: the flap flip, and only on a state change

## Colors

### Primary
- **Ink** (`{colors.ink}`): every label, figure, and plan name. Almost nothing is set in a lighter value.
- **Amber** (`{colors.amber}`): the action colour and the in-flight status. It is the *only* filled colour on a surface, and it belongs to exactly one control per view.

### Secondary
- **Green** (`{colors.green}`): complete, verified, unlimited — the "yes" column of a comparison and the "done" status of a row.
- **Red** (`{colors.red}`): held, revoked, expired. It never appears in marketing copy, only in a real state.

### Neutral
- **Board** (`{colors.board}`): the instrument surface behind everything.
- **Panel** (`{colors.panel}`): a board's own ground.
- **Flap** (`{colors.flap}`) / **Flap deep** (`{colors.flap-deep}`): the row module and its seam.
- **Slot** (`{colors.slot}`): an empty cell and every numeric readout window.
- **Ink dim** (`{colors.ink-dim}`): units, secondary columns, helper lines.

### Dark Theme
This world is **dark-first**: the board is charcoal, and the light theme is the same panel under
daylight (`--board-light` family: a cool grey panel). Light is not an inversion of roles — amber stays
the action, green stays the verified state, only the ground and ink swap places. Both themes are
required; a surface that only works in one is unfinished.

### Named Rules

**The Status-Only Colour Rule.** A colour may appear only as a status or an action. Decorative
gradients, tinted section grounds, coloured headings and coloured borders for mood are defects. If a
value's meaning cannot be named as *act / done / blocked*, it renders in ink.

**The One Primary Rule.** One amber filled control per view. A second amber button splits the
decision and is a defect, even when two plans are both purchasable.

## Typography

**Display / labels:** Barlow Condensed (SIL OFL, Vietnamese subset), weights 500–700, always uppercase,
tracked `0.1em`–`0.22em` depending on size.
**Data:** Martian Mono (SIL OFL) with `font-variant-numeric: tabular-nums` — figures, prices, byte
counts, percentages, row codes.
**Body (shell):** Archivo (SIL OFL) for anything that is a sentence rather than a label.

**Character:** the voice is an announcement, not a conversation. Names are short capitals; numbers are
monospaced so a column of them reads as one object; prose is rare and small.

### Hierarchy
- **Display** (700, `clamp(1.75rem, 4vw, 3rem)`, line-height 0.95, uppercase): the page's one statement.
- **Headline** (600, 1.1rem, tracked 0.12em, uppercase): a panel title.
- **Label** (400, 0.5rem, tracked 0.14em, uppercase, mono): column headers, row keys, units.
- **Data** (400, 0.65–0.72rem, mono, tabular): every figure in a row or cell.
- **Body** (400, 0.85rem, Archivo): the lede and helper lines only.

### Named Rules

**The Tabular Rule.** Any figure that sits in a column carries `tabular-nums`. A price that shifts
width when the billing period changes is a defect.

**The Unit-Attached Rule.** A number never appears without its unit in the same node — "2 TB", "5 GB",
"42 giây". A bare `2147483647` is not a limit, it is a bug; unlimited is rendered as a word.

## Layout

Boards are a grid of one label column and three value columns (`1.25fr repeat(3, 1fr)`), the same
ratio in the head, the rows and the action row beneath it, so all three align down the page. Panels
sit inside the app shell with a 1px seam and no radius; the shell keeps its own chrome for now.

Breakpoints: **900px** is the board's own breakpoint. Above it, a comparison is a table; below it, a
table is replaced by stacked per-item blocks with every value labelled. There is no intermediate
state where columns lose their headers.

### Named Rules

**The Label-Or-Nothing Rule.** On a narrow screen every value carries its own label. A value that can
only be understood by its column position above it does not survive the breakpoint, so the table is
replaced rather than squeezed.

**The Seam-Depth Rule.** Structure is expressed with seams: 1px borders, an inset 1px dark line under
a row, and a 1px bright line on top of a filled cell. No blur, no gradient, no drop shadow.

## Elevation & Depth

There is no elevation. A panel sits on the board, a row sits on the panel, a cell sits in the row, and
each boundary is a 1px seam. The only two optical effects permitted are the flap seam (a hairline
through the middle of a filled cell) and the inset shadow that makes a row read as slightly recessed.
Modals and popovers inherit the panel treatment with a seam, not a shadow.

### Named Rules

**The No-Blur Rule.** `box-shadow` with a blur radius, `backdrop-filter`, and gradients used as
lighting are defects in this world. The one gradient allowed is a status fill's own two-stop ramp,
and only when a cell is animating.

## Shapes

Everything is square: panels, rows, cells, buttons, tags, inputs. Radius is `0px` at token level and
`!important` on controls that a library would otherwise round. The silhouette is a timetable grid, and
a rounded control inside it breaks the reading.

There are no icons. A state is a colour and a word; a column is a header; an affordance is a bordered
word ("Sửa", "Đổi", "Xem"). Emoji, unicode tick-marks and icon fonts are defects — the comparison
table's `✓`/`—` were replaced with translated words for exactly this reason.

### Named Rules

**The Square-Instrument Rule.** No radius anywhere in a board surface, including tags and toggles.

**The Word-Instead-Of-Glyph Rule.** If a state needs a symbol, it needs a word instead. This includes
checkmarks in tables.

## Components

### Board
Head row (`1.25fr repeat(3,1fr)`) with a 0.5rem tracked label above the plan name, the recommended
column filled with `flap`, then one row per fact. Rows alternate nothing: the seam separates them.

### Row / Flap
`background: flap`, `border-top: 1px flap-deep`, `box-shadow: inset 0 -1px 0 rgba(0,0,0,.45)`,
10px vertical padding, 10–12px horizontal. A key cell is a 0.5rem tracked label; a value cell is mono,
centred in a comparison and right-aligned in a data row.

### Cell (status)
20–22px tall, `slot` when empty; `green` when done with a 1px dark seam across its middle and a 1px
bright line on top; `amber` while in flight; a 45° red hatch when held. Width is `1fr` so a row of ten
fills the board rather than sitting in a stub at the left.

### Actions
Primary: amber fill, `#16181A` label, square, 0.72rem tracked uppercase. Secondary: transparent with a
1px hairline. A plan already owned renders as a green tracked word, not a disabled button.

### Meter
6px `slot` track, `green` fill, 1px `amber` marker at the fill head. No rounding, no animation of
width beyond the world's single 260ms flip; `prefers-reduced-motion` removes it.

### Money
`amount` in Barlow Condensed 1.15rem amber with the period as a 0.5rem tracked mono unit beneath. The
billing period is **server state** carried in the URL; the page renders one period's figures, never
both.

## Do's and Don'ts

### Do:
- **Do** set every structural boundary as a 1px seam in `hair`, and every filled cell with an inset
  dark line plus a bright top line.
- **Do** keep one amber control per view and make every other action a hairline secondary.
- **Do** render numbers in Martian Mono with `tabular-nums` and their unit attached.
- **Do** carry the billing period (or any similar toggle) as a URL parameter rendered server-side, so
  the page is correct with JavaScript off.
- **Do** replace a comparison table with labelled blocks below 900px.
- **Do** state "Unlimited" as a word when a config value is the unlimited sentinel.
- **Do** verify both themes on the built surface before calling it finished.

### Don't:
- **Don't** add radius, blur, gradients or lifts to a board surface.
- **Don't** colour anything that is not act / done / blocked.
- **Don't** use a unicode glyph or icon where a word will do.
- **Don't** hardcode a figure that exists in `PlanTierLimits` / `PlanCatalog`; read it.
- **Don't** add a second amber button to a pricing view.
- **Don't** mix the legacy `db-*` token language into a migrated component.

## Honest Gaps

Recorded so the next contributor does not mistake intention for implementation.

- **The site chrome now carries this world**: `MainLayout` nav and footer are a flap strip and its
  seam, so every page shares one instrument. Page grounds were deliberately left alone — an
  unmigrated body keeps its own paper so its text keeps its contrast. `/pricing` (`Pricing.razor` + `.razor.css`), `/send`
  (`Upload.razor` + a board layer appended to `.razor.css`), `/d/{code}` (`Download.razor` + the same
  kind of appended layer), `/login` (`Login.razor` + the same kind of layer) and the `/account` usage
  block (`AccountUsage.razor` + `.razor.css`). Home, `/account`'s other cards and the rest of the shell
  are still HyperCard/DAUB. The `/account` hub shell is now board material too (`Account.razor.css`
  reaches `[data-slot="card"]` through `::deep`), but its cards were **not** individually art-directed:
  they inherit colour, type, seams and control shapes only.
- **`/account` was never viewed signed in.** Dev has no seeded session, so the hub's new shell is
  guarded only by tests (`Account*`, `PageRenderingTests` — 52 passing), not by a screenshot.
- **`/d/{code}` was verified only in its not-found phase.** The dev run has no object store, so a real
  delivery could not be rendered; `DownloadNoScriptTests` (which the no-JS contract depends on) passes,
  but the ready/downloading/verified phases were not eyeballed in-app.
- **`/send` was migrated as a material layer, not a rewrite.** Its markup, ids and upload JS are
  untouched; the board tokens live on `.upload-board` and override colour, type, radius, depth and
  control styling. The legacy spacing/grid rules still place its panels, so the page reads as the
  board instrument with the old skeleton underneath. Auxiliary panels (camera, voice, image editor,
  video trim, PDF tools, cloud import) inherit the tokens but were not individually art-directed.
- **`::deep` is required for controls rendered by library components.** Elements produced by
  `Input`/`Button` do not carry the page's scope attribute, so `.upload-board input` silently missed
  the password field; the rules must be written `.upload-board ::deep input`.
- **Board tokens are page-scoped CSS variables**, duplicated per component, not global tokens. There
  is no `--b-*` layer in `app.css` yet, so a new board surface must copy them.
- **The world's best compositions exist only as mocks**: the split-flap `/send` board and the `/d/{code}`
  arrivals board are `.impeccable/mocks/style-review/worlds/04-send.html` and `04-download.html`, not
  Blazor pages.
- **Mobile was verified for the mocks, not for the in-app pages.** `/pricing` below 900px and the
  `/account` usage block were not captured at 390px in the built app.
- **Light theme was not captured in-app.** The palette is implemented for both (`html[data-theme="light"]`
  blocks in both scoped stylesheets) but nobody has eyeballed it on a built page.
- **Two font families were added to `App.razor` for this world** (Barlow Condensed, Martian Mono). They
  load on every page, including legacy ones, which is a small payload cost accepted until the
  migration is finished.
- **The in-app `/pricing` still mixes shadcn components** (the calculator's `Card`, `Input`, `Button`)
  with the board language. They are transitional; migrating them is the next surface's job.
- **`/account` remains a hub of legacy cards.** Only the usage block speaks the board grammar, so the
  page reads as one instrument among HyperCard panels until the rest is migrated.
