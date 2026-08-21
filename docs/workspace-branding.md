# Workspace branding

Issues `#285` (branded recipient page) and `#286` (custom typography). One entity, one
migration, and one surface: the pages a workspace's *clients* open.

## Scope: the recipient page, not the app

A brand applies to the delivery page and the upload success page — the moment an agency's client
sees who sent them the file. It deliberately does **not** re-skin the signed-in application: a
member of two workspaces switching between them would lose any stable sense of where they are,
and the app chrome is our product, not the customer's.

`HideQuickShareBranding` removes the wordmark from recipient pages, which is the brand half of
`#81` white-label. It removes *marketing*, not disclosure — the page still says who processed
the file where that is legally required.

## Data model

One row per workspace (`workspace_brands`): enabled flag, logo key, accent pair, header and
footer copy, the white-label switch, two font keys with their family names, and a licence note.

`IsApplicable` gates rendering: enabled **and** something to actually show. An enabled but empty
brand renders nothing rather than an empty header band.

## Type sizes are not configurable

`DESIGN.md` owns the typography scale. A brand swaps *families*; it cannot change sizes.

A brand that can resize headings can break every layout it touches, and the resulting page is
not "on brand", it is broken. "Your font, our rhythm" is the trade that keeps the page readable,
and the CSS renderer emits no `font-size` at all — which is asserted in the tests, because that
is the kind of constraint that erodes one convenient exception at a time.

## Rendering

`BrandReader.ToCss` emits `@font-face` rules plus a `:root` block that overrides **token values
only, never token names**. That is the same rule `DESIGN.md` states for the vendor layer, and it
is why ~322 uses of `db-*` classes keep working.

The stylesheet is server-rendered into `<head>` on the delivery page, so a client's first paint
is already the agency's rather than a flash of ours.

**Everything interpolated into that CSS was validated on save** — hex colours by regex, font
family names against `^[A-Za-z0-9 \-]{1,60}$`. Nothing user-supplied can close a declaration and
open something else. That is what makes the `MarkupString` on the page safe, and it is why the
validation lives at the write boundary rather than at render time.

Font faces use `font-display: swap` and keep a fallback stack. A download button hidden behind a
font request that may never finish is a worse failure than the wrong typeface.

## Uploads

Logos: PNG, JPEG or WebP, 512 KB. Fonts: woff2, 512 KB.

- **Sniffed by magic bytes, not by the declared content type**, which is only the uploader's
  claim about their own file.
- **SVG is refused.** It is a document format that can carry script, and an agency logo is not
  worth turning a client's page into an XSS surface.
- **GIF is refused** too — an animated logo on a page where somebody is reading a contract is
  not a choice worth offering.
- There is no image library in this project, so a logo is **not decoded**. The size cap and the
  signature are the guard, and the file is only ever rendered inside an `<img>` or an
  `@font-face`, where a mismatched file fails to render rather than executing anything.
- **Replacing an asset writes a new key** and deletes the old one. The public route carries a
  far-future immutable cache header, so an in-place overwrite would leave a client's browser
  showing last month's logo.

## Serving assets

`GET /brand/{workspaceId}/{kind}` is **anonymous** — the audience is a client with no account
here. That is safe: the response is a logo or font the workspace chose to publish, and the
workspace id grants nothing else.

Streamed through the app rather than presigned: a presigned URL expires while the page is still
open, and a public bucket is not something a self-hoster necessarily has. `nosniff` is set,
because the bytes were sniffed but never parsed.

**A disabled brand serves 404.** Switching a brand off has to take it off the client's page, not
merely stop linking to it.

## Contrast

The accent pair is checked at 3:1 (WCAG AA for large text and UI components) using the real
relative-luminance formula, with three-digit shorthand expanded so both forms measure the same
colour.

A brand that can make its own recipient page unreadable is not self-expression; it is a support
ticket from the client who could not find the download button.

## Caching

`BrandReader` caches per workspace for 30 minutes — the recipient page is the hottest page in
the product and a brand changes about as often as a company rebrands. Eviction is explicit from
every write path. A stale logo is a small wrong; a stale *removed* logo is an agency's old
identity still on a client's screen after they asked for it to go.

## Extending this

- **New recipient-facing page?** Resolve the brand from the *delivery's* workspace, not the
  viewer's — the recipient has no account, and the brand they should see is the sender's.
- **Adding a brandable property?** Override a token *value*. Never add a token name, and never
  make a type size configurable.
- **Adding an uploadable asset type?** Sniff its signature, cap its size, and think about
  whether the format can carry script before accepting it.
- **A brand write path that skips `BrandReader.EvictAsync`** will serve a stale brand for up to
  thirty minutes.

## Not shipped

- **Per-folder brand override.** `#285` mentions it; a second resolution layer for a setting
  agencies change once a year is not worth the lookup on every recipient page.
- **Custom domain (`#67`) carrying the brand.** That issue is still open, so `#81` white-label
  remains open too — brand control without domain control is not white-label, and closing it
  would be a claim we cannot back.
- **Automatic legibility scoring of an uploaded font.** The contrast check is real; judging
  whether a typeface is readable is not something this can honestly automate. The size floor from
  `DESIGN.md` is the actual protection.

## Tests

`tests/QuickShare.Server.Tests/E2E/WorkspaceBrandTests.cs` (16) — colours and copy round-trip;
non-hex colour and CSS-escape attempts refused; unreadable accent pair refused; contrast maths
checked against the WCAG formula including shorthand; font name that would escape the
declaration refused; PNG accepted and served with `nosniff`; SVG and non-woff2 refused; replacing
an asset changes its key; a disabled brand serves 404; unknown workspace or kind 404s; admin
gating; the emitted CSS overrides values with a fallback stack and no `font-size`; the brand
appears on a workspace delivery page and does **not** leak onto a personal one.
