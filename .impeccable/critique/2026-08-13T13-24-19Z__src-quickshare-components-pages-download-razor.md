---
target: /d/{code} download page
total_score: 14
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
timestamp: 2026-08-13T13-24-19Z
slug: src-quickshare-components-pages-download-razor
---
Method: dual-agent (A: a6d9760a5df18c39f · B: a76edd4fbb4271077)

Surface: `/d/{code}` — the public download page. Mode: Operate. This is the highest-traffic
surface in the product: every share link sent equals one visit, almost all by strangers to the
product, mostly Vietnamese, mostly on phones, arriving from Zalo/Facebook.

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 2 | `fetchBytes()` buffers the whole file via `arrayBuffer()` — no progress, no %, no ETA |
| 2 | Match System / Real World | 1 | All strings hardcoded English for a Vietnamese audience; surrounding chrome is localized |
| 3 | User Control and Freedom | 2 | No cancel on in-flight decrypt; `Error` state offers no retry and no link |
| 4 | Consistency and Standards | 1 | Four button systems on one card; `<PageTitle>` ships mojibake (`C3 A2 E2 82 AC E2 80 9D`) |
| 5 | Error Prevention | 1 | All three phases ship `class="hidden"` (lines 39, 64, 80) — any JS failure yields an empty card |
| 6 | Recognition Rather Than Recall | 2 | No sender identity shown; expiry stated twice in two different phrasings |
| 7 | Flexibility and Efficiency | 1 | Six OTP inputs with `autocomplete="off"`, no paste — a code copied from Zalo must be retyped |
| 8 | Aesthetic and Minimalist Design | 2 | Card competes with 5-item navbar, cookie dialog, SEO footer, and an AdComponent that renders nothing for anonymous visitors |
| 9 | Error Recovery | 1 | Download failure only surfaces via `DAUB.toast` behind `if (window.DAUB)` — silent if DAUB has not loaded |
| 10 | Help and Documentation | 1 | No answer to "what is this site"; `zk-panel` explains circularly |
| **Total** | | **14/40** | **Poor — overhaul, not polish** |

No heuristic n/a: Operate surface, all ten bind.

## Design Specificity Verdict

LLM assessment: the page is category-interchangeable. Centered `max-width: 28rem` card, 4rem
rounded file icon, filename H1, pill badges, one full-width button — nothing here could only
exist in a zero-knowledge product. The one true differentiator (the key never leaves the
browser; it lives in the URL fragment) is expressed as a grey pill reading "End-to-end
encrypted", and the `crypto.subtle.decrypt` moment renders as the word "Decrypting…" with a
default spinner. Colour hierarchy is inverted: the primary CTA is neutral ink
(`--db-color-text`), the loudest colour on the card belongs to the "Verified" badge tinted with
`--db-color-warning` (an amber trust badge), and the brand accent is spent on a decorative icon
square. Decoration gets the brand, trust gets a warning hue, action gets neutral.

Deterministic scan: `detect.mjs` returned `[]` for this surface, but that is NOT evidence of
cleanliness. `.razor` is absent from `SCANNABLE_EXTENSIONS` (`detector/node/file-system.mjs:26-30`),
so a directory target scanned zero markup files, and explicitly-passed `.razor` files went through
the generic text engine with no DOM parse. Treat as unavailable coverage. App-wide scan: 7
`layout-transition` warnings, all outside this surface.

Visual overlays: none. Port 5000 is held by an unrelated `Salad.Bowl.Service`; the app needs
Postgres + R2 to boot, so no server was started for a read-only critique. All a11y findings below
were read from source, not from computed styles.

## What's Working

1. `handleDownload()` reuses `state.bytes` from the preview fetch, so previewing then downloading
   does not burn a second decrement against the sender's `maxDownloads`. Real product thinking.
2. The `render()` phase machine is honestly one-thing-at-a-time — hide all three, show one. The
   sequence is enforced in code rather than hoped for in CSS.
3. `renderText()` uses `textContent`, never `innerHTML`, with the reasoning committed inline. On a
   page rendering one stranger's file content to another, this is the line between a feature and a
   stored-XSS delivery mechanism.

## Priority Issues

[P0] All three content phases ship hidden; JS is a single point of total failure.
- What: `download-phase-otp` (39), `-ad` (64), `-file` (80) all carry `class="hidden"`
  (`display:none !important`). Only `download.js` `init()` removes it, and `init()` self-reschedules
  forever if `#download-info-data` is missing.
- Why: highest-traffic surface, opened in Zalo/Facebook in-app webviews on cheap Android over 3G.
  CSP, ad-blocker, aborted load, or an old webview lacking `crypto.subtle` yields a card containing
  nothing at all — not an error, nothing.
- Fix: the server already knows `Info.HasOtp` and `Info.SenderPlan`. Pick the initial phase in Razor
  and let JS only transition. For unencrypted files ship a real `<a href="/api/downloads/{code}">`
  that works with zero JS, and let JS upgrade it.
- Suggested command: /impeccable harden

[P1] Nothing says who sent the file — the layout is the exact shape of a phishing page.
- What: a stranger sees a filename, a size, a big Download button. Trust rests on three
  self-asserted rows in the collapsed `zk-panel` ("verified via cryptographic attestation", "No
  tampering detected") plus two badges.
- Why: those are unfalsifiable claims made by the party asking you to download. That is performance,
  not information, and it is the register phishing uses. It also throws away the one claim that
  would be verifiable — that the recipient's own browser did the decryption.
- Fix: show the sender (or say "sent anonymously" honestly). Replace "Verified" with the mechanism
  in the present tense of what just happened on this device. Show the real SHA-256 prefix as
  monospace instead of the sentence "SHA-256 hash matches".
- Suggested command: /impeccable clarify

[P1] English-only page inside `<html lang="en">` on a localized product.
- What: every string in `Download.razor` is a hardcoded English literal while `MainLayout`,
  `CookieConsent` and `AdComponent` use `IStringLocalizer<SharedLoc>` and a `vi` assembly ships.
  The footer directly below reads "Gửi ảnh không nén" next to "Online clipboard".
- Why: a half-translated page reads as a scraped or cloned site, not a product — on the exact
  surface where a stranger is deciding whether to trust it.
- Fix: move the strings into `SharedLoc`; set `lang` from `CultureInfo.CurrentUICulture`.
- Suggested command: /impeccable clarify

[P1] Focus rings suppressed globally with no replacement, plus no accessible names or live regions.
- What: `app.css:283-287` sets `outline: none !important` on all `input:focus-visible` (including the
  six OTP fields), substituting a border-colour change — a colour-only focus indicator.
  `app.css:291-298` strips both `outline` and `box-shadow` from `.db-btn:focus` / `.db-btn--ghost:focus`,
  which covers the preview Copy and Show-full-text buttons. `Download.razor.css` has no
  `:focus-visible` rule at all. The six OTP inputs have no accessible name; `#dl-otp-error` has no
  `role="alert"`; `#dl-zk-btn` has no `aria-expanded`/`aria-controls`; there is no `aria-live`
  anywhere in the file against 11 JS-driven state changes.
- Suggested command: /impeccable audit

[P2] The ad interstitial is self-promo with a fake countdown and a working skip button.
- What: `download-phase-ad` is titled "Powered by QuickShareCloud", sells encryption, links to
  `/pricing`, and its "Skip ad" button is enabled at t=0 and cancels the timer instantly. The
  separate `<AdComponent />` on the file card early-returns for anonymous users — i.e. renders
  nothing for ~100% of this page's traffic.
- Why: it taxes a stranger to monetize somebody else's free plan, and is trivially bypassed, so it
  collects annoyance without revenue.
- Fix: delete both from this page. If free-plan messaging must appear, make it one honest line after
  the file is saved.
- Suggested command: /impeccable distill

[P2] Nothing confirms the download happened; large files have no progress and no size guard.
- What: `saveBytes()` clicks a synthetic anchor and returns; the button reverts to "Download".
  `fetchBytes()` calls `arrayBuffer()` on the whole file with no progress events and no ceiling.
- Why: peak-end ends in nothing, indistinguishable from failure on iOS Safari; a multi-GB file OOMs
  the tab after minutes of an unlabelled spinner.
- Fix: persistent success state ("Saved — {filename}"); stream via `response.body.getReader()` with a
  byte progress bar; warn on size over cellular before starting.
- Suggested command: /impeccable harden

## Persona Red Flags

Casey (distracted mobile, one-handed, slow connection): the fixed-bottom `CookieConsent` dialog lands
in the thumb zone over `dl-download-btn`, so an ad-personalization banner must be dismissed before
touching the file. "Skip ad" is `--db-text-xs` with no padding — a ~14-16px hit area in the
lowest-contrast text on the page. The filename is `white-space:nowrap` + ellipsis with the full name
only in `title`, so on touch a long Vietnamese filename can never be read in full. Backgrounding the
tab throttles `setInterval`, leaving the countdown stuck.

Jordan (first-timer, does not necessarily read English): every word on the task path is untranslated.
`zk-panel` says "cryptographic attestation" and "the original upload manifest". The `Error` state is a
dead end with no button, no retry, no home link, while Expired/NotFound offer "Send a file" — a CTA
that converts a failed recipient into a sender rather than helping them get their file.

Sam (screen reader + keyboard): six unlabeled OTP edit fields; `otpInput()` moves focus on every
keystroke, fighting forms mode; OTP errors are silent; `#dl-zk-btn` announces as a button that does
nothing; phase changes have no live region and no focus management, leaving focus on a hidden button
after a successful unlock. `download.js:28` replaces `dl-download-icon` via `outerHTML`, destroying the
node so it never returns and a later `render()` throws on null. `<pre id="dl-text-body">` scrolls but
has no `tabindex="0"`, so keyboard users cannot scroll the preview.

## Minor Observations

- `<PageTitle>` contains double-encoded UTF-8: the tab reads `Download â€" QuickShareCloud`.
- `window.ZAPDROP_CONFIG` — a dead former product name inlined into every recipient's HTML.
- `--db-color-surface-2` (`Download.razor.css:245`) is defined nowhere, so the text preview always
  falls back to `rgba(0,0,0,0.03)` — nearly invisible on cream. It is also the only hardcoded colour
  literal in the file.
- `Download.razor.css` has no width breakpoints at all; the only `@media` is `prefers-reduced-motion`.
- `.input:focus` is defined but `.input` is never used in `Download.razor` — dead rule. `.--warn` is
  defined and unused. `.layout-avatar-btn:hover {}` is an empty rule in `MainLayout.razor.css`.
- `@attribute [StreamRendering]` with an unguarded `OnInitializedAsync` DB query takes the fallback
  path that CLAUDE.md ranks below HTMX lazy-load.
- `maybeRenderTextPreview()` auto-fetches and decrypts up to 1 MB before the recipient asks for
  anything, spending their data on a metered connection without consent.
- 41 inline `style="…"` attributes in `Download.razor`.

## Questions to Consider

1. The most distinctive thing this product does happens invisibly in `crypto.subtle.decrypt` on the
   recipient's phone. What would this page look like if that moment were the hero instead of the file
   icon?
2. If every unverifiable sentence were deleted from `zk-panel`, what remains — and would the page be
   less trustworthy, or more?
3. Nearly all traffic here is Vietnamese recipients on phones from Zalo. The nav offers Send, P2P,
   Pricing, Login and a language switcher; the footer offers SEO pages; a cookie dialog covers the CTA.
   Which of those survives the test "this stranger came here to get a file"?
4. Is a 5-second tax on a stranger, to monetize someone else's free plan, with a working skip button,
   a business model or a superstition?
