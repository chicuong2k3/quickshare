# shadcn/ui for Unpoly.Blazor

A port of [shadcn/ui](https://ui.shadcn.com) to Blazor **static SSR** with
[Unpoly](https://unpoly.com). Same token names, same class strings, same `data-slot` contract,
same `buttonVariants` export. Copy-in source, not a package: every component is a file you are
expected to edit.

```
src/Unpoly.Blazor.Shadcn/
  Styles/ui.css            tokens + @theme inline  (import this from the app's app.css)
  Styles/ui.behavior.css   what Radix does in JS, done by the platform
  wwwroot/ui.js            the compilers  (must be in the app's @source list)
  Components/*.razor       the components — class lists are GENERATED, see below
  ButtonVariants.cs        shadcn's `buttonVariants`, for things that are not buttons
  LucideIcons.g.cs         the Lucide glyphs, generated from lucide-static
  UiComponentBase.cs       Class / ChildContent / AdditionalAttributes, and `Cn`
upstream/                  vendored shadcn/ui .tsx — what parity is measured against
deviations.json            every difference from shadcn, with its reason
tools/                     the generators; all six take --check and CI runs them
skills/shadcn-unpoly/      SKILL.md, a generated API index and BLOCKS.md, for coding agents
themes/                    Material 3, Cupertino and Fluent 2, and what each cannot reproduce
demo/                      every component with its source, the blocks, a three-palette theme switcher
```

## The demo

```bash
dotnet run --project demo/Unpoly.Blazor.Shadcn.Demo
```

Every example is **rendered and shown from the same file** — the source under each component is
read from the `.razor` that produced it, not retyped beside it. Docs that carry a copy of their
own code start lying the first time someone edits one half, and this library exists partly to
make that class of drift impossible.

`/blocks` has six whole sections meant to be copied: sign in, page header, stat cards, a data
table with a toolbar, settings, and an empty state. Each carries the Unpoly wiring that makes it
behave, which is the part that is easy to leave out and impossible to notice missing —
`skills/shadcn-unpoly/BLOCKS.md` names the trap in each.

### Tailwind 3 demo default (consumer migration pending)

Both demos now build and load `app.v3.css` by default, including with JavaScript disabled.
They also build Tailwind 4 `app.css` for comparison: visit `/?demo-css=v4` to switch
for the current tab (the choice persists across navigation), or `/?demo-css=v3` to return.
Do not build with `-p:Tailwind3Preview=false`: the build fails rather than ship a missing
stylesheet. POS and Portal now reference this tracked vendored library for their binaries, static assets and CSS sources, but still compile/load **v4** `app.css`; no consumer v3 host switch has shipped.
For Windows MAUI, `node tools/maui-webview-smoke.cjs` starts a disposable real WebView2
profile and checks selected interactions; use `--baseline` for v4 or `--inventory-only`
for the two standalone selection primitives. `node tools/web-inventory-browser.cjs
<chromium.exe>` checks both Web host stylesheets and compares eight light/dark,
narrow/wide example screenshots. The demo inventory (`node tools/audit-demo-components.cjs`)
now reports zero unreferenced component sources in both heads, but composition and
direct example tags are not full per-component state/interaction coverage. Add
`--keep-open --review-only` to leave a v3 MAUI window open for inspection.
The demo stylesheet is not Safari 15 certification or a POS/Portal stylesheet; real Safari 15
is owner-waived (NOT RUN), and full component coverage remains a release gate. See
`tools/README.md` for build and probe details.

## Parity, and how it is checked

The class strings are not typed by hand. The `fetch-upstream` .NET command downloads shadcn's own
source into `upstream/`, `extract-upstream` reduces it to class sets, and `sync-classes` writes
them into the components. The parity tests then render every
component and compare it with the same upstream data.

That indirection is the point. Parity asserted against a class string someone retyped from the
docs is not parity — it is the same memory twice, and it agrees with the implementation for
exactly the reason the implementation is wrong.

`deviations.json` is the complete list of differences, each with a written reason. Both the
generator and the tests read it, so they cannot disagree about what counts as one. Anything not
in it must match shadcn exactly, and the test fails if it does not. At the time of writing there
are eight, and four components with no upstream counterpart at all.

## Wiring a head

```css
/* app.css */
@import "tailwindcss";
@import "../../Unpoly.Blazor.Shadcn/Styles/ui.css";

@source "../Components/**/*.razor";
@source "../../Unpoly.Blazor.Shadcn/Components/**/*.razor";
@source "../../Unpoly.Blazor.Shadcn/ButtonVariants.cs";
@source "../../Unpoly.Blazor.Shadcn/wwwroot/ui.js";   /* ui.js builds DOM out of Tailwind classes */

:root {
    --primary: #a02742;          /* … the rest of the shadcn palette */
    --control-h: 2.875rem;
    --control-text: 15px;
}
```

```razor
@* App.razor *@
<link rel="stylesheet" href="_content/Unpoly.Blazor.Shadcn/ui.safari15.css" />
<script src="_content/Unpoly.Blazor.Shadcn/compat/safari15-shim.js"></script>
<script src="_content/Unpoly.Blazor.Shadcn/compat/dialog-shim.js"></script>
<script src="_content/Unpoly.Blazor.Shadcn/compat/popover.iife.min.js"></script>
<script src="_content/Unpoly.Blazor.Shadcn/compat/css-has-pseudo.js"></script>
<script src="_content/Unpoly.Blazor.Shadcn/compat/has-pseudo-boot.js"></script>
<script src="_content/Unpoly.Blazor.Shadcn/toastify/toastify.js" defer></script>
<script src="_content/Unpoly.Blazor.Shadcn/ui.js" defer></script>
```

The five compatibility assets must be loaded before `_content/Unpoly.Blazor/unpoly.min.js`.
They are required for Safari 15: the shim supplies missing JavaScript APIs, the `<dialog>` shim
supplies `show()`, `showModal()` and `close()` on Safari 15.0-15.3 (without it `Dialog`,
`AlertDialog`, `Sheet`, `Drawer` and `CommandDialog` cannot open there), the popover
polyfill supplies the missing top-layer API, and the `:has()` assets cover Safari 15.0-15.3.
The `<dialog>` shim has no top layer: a modal is `position: fixed` with a high `z-index`, so an
ancestor that creates a stacking context can cap it. Nor is there `inert` there: Tab is trapped and
the rest of the page is `aria-hidden` while a modal is open, which fences screen readers and the
keyboard but not a script that moves focus out of the dialog.
Applications targeting Safari 16+ may omit them, except that `ui.safari15.css` is harmless
and can be kept in a shared layout.

**`ui.js` is wrapped in an IIFE, and must stay that way.** A classic `<script src>` shares one
global scope with every other script on the page. `const el` here against `function el` in a
head's own `app.js` is a redeclaration, and the browser throws a SyntaxError that kills the whole
file before one compiler is registered — no server error, nothing at the call site, every dialog
and dropdown simply absent on the one head that picked the same name. It cost an afternoon.

**`@source "…/ui.js"` is not optional.** Tailwind emits only the classes it can see, and the
Select panel, the dropdown and the confirm dialog are assembled in JavaScript. Leave it out and
those three render unstyled, silently, in production only.

## Where this differs from shadcn, and why

Radix keeps component state in React. Nothing here can: the server renders once and Unpoly swaps
fragments underneath. So each interactive component is either a native element that already owns
the state, or an `up.compiler` that rebuilds after every swap and tears itself down.

| Component | shadcn / Radix | Here |
|---|---|---|
| `Dialog`, `AlertDialog` | portal + overlay + focus trap in JS | native `<dialog>` + `showModal()` |
| `Accordion` | reducer, `data-state` | `<details>` / `<summary>`; `Name` gives you `type="single"` |
| `DropdownMenu` | Popper + portal | `[popover]` for the top layer, `ui.js` for the position |
| `Select` | listbox of divs + hidden input | real `<select>` + a shadcn panel drawn beside it |
| `Checkbox`, `Switch`, `RadioGroupItem` | `<button role=…>` + hidden input | the real input, styled; demo select-all derives a real mixed state from children, and each radio example needs its own `name` |
| `Tabs` | React state | `ui.js`, panels hidden with `hidden` so their fields still post |
| `Sonner` | its own renderer | Toastify-js, same `toast()` / `toast.error()` call shape |
| `Form` | react-hook-form `Controller` | `<FormField>` takes Label/Description/Message as parameters |

Three API differences follow from that and cannot be papered over:

- **No `asChild`.** Blazor has no `cloneElement`. The case it was used for — "render this as a
  link" — is the `Href` parameter on `Button`, `Badge` and `DropdownMenuItem`.
- **No `<SelectTrigger>` / `<SelectValue>` / `<SelectContent>`.** The native `<select>` is all
  three. Options are `<SelectItem>`, which renders `<option>`.
- **No `TooltipProvider`.** Each tooltip keeps its own delay timer, so the grouped
  "one opens, the rest open instantly" behaviour is missing.
`cn()` is the exception that used to be on this list and is not any more: `ClassMerge` runs
[tailwind-merge](https://github.com/dcastil/tailwind-merge) through its .NET port, with this
library's three tokens registered in the groups they stand in for, so `Class="h-12"` removes the
variant's `h-control` exactly as it would in React.

Two tokens are ours: `--control-h` and `--control-text`, surfaced as `h-control` and
`text-control`. shadcn writes `h-9 text-sm` literally, which pins every consumer to one
control size. Everything else is shadcn's string, unchanged.

## Four components shadcn does not have

Each says so in its own comments, and each is skipped by the parity tests because there is
nothing upstream to compare it with:

- `DatePicker` — shadcn's is a Popover around react-day-picker; this is Air Datepicker,
  attached by a compiler so it survives a fragment swap.
- `TagsInput` — Tom Select over a real `<select multiple>`, so the form posts without JavaScript.
  shadcn's nearest is Combobox, which is single-value and built on cmdk.
- `Stepper` — shadcn has no number stepper, and a bare `<input type=number>` is not one: its
  spinners are a different shape in every engine and invisible until hovered.
- `Icon` — lucide-react ships one component per icon; 2000 Razor files is not an icon set, it is
  a build problem, so one component takes the name.

## Unpoly

No component knows Unpoly exists. Every one splats `AdditionalAttributes`, so `up-target`,
`up-poll`, `up-validate`, `up-disable` and the rest are written as plain attributes at the call
site. The single point of contact is a CSS rule: `[data-slot="button"].up-active` grows a
spinner, because Unpoly marks the element that started a request and a submit button is the
honest place to show it.

Two things worth knowing before reaching for a component here:

- **An Unpoly overlay beats `<Dialog>` when the content comes from the server.**
  `up-layer="new"` renders a *real route* into a modal, keeps it bookmarkable, and lets the
  server decide what goes in it. `<Dialog>` is for content the page already has.
- **`<Skeleton>` rendered by the server is never seen.** Static SSR runs
  `OnInitializedAsync` to completion before a byte exists. The skeleton a user sees comes from
  `[up-placeholder]`, so these live in `<template>` in the layout, not in the page they stand
  in for.

## Adding a component

Copy the class strings from ui.shadcn.com and change three things: `text-sm` → `text-control`
and `h-9` → `h-control` on controls, `data-slot` on the root, and `@attributes` last so a caller
can override. If it needs state, ask in this order — does a native element already have it, does
an Unpoly attribute already do it, and only then write a compiler. A compiler must return a
destructor; one that leaves residue behind does not break this swap, it breaks the next one.
