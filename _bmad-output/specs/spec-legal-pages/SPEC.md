---
spec: legal-pages
date: 2026-08-14
companions: []
sources:
  - "GitHub issue #93 — [Trust] Legal pages ToS + Privacy Policy"
assumptions:
  - Template-based legal text without lawyer review; pages carry a dated version so a reviewed revision can supersede.
open_questions: []
---

# SPEC — Legal pages (ToS + Privacy Policy)

## Why

ToS + Privacy Policy are the legal contract the service currently lacks. Deletion (#59), DMCA (#53), zero-knowledge claims (#26), and payments (#85) all need legal grounding; EU DSA and VN Decree 13/2023 both require these documents. Shipping without them is liability.

## Capabilities

- **CAP-1** `/terms` page, vi + en.
  - intent: service description, acceptable use, liability limits, refund policy (PayOS payments), account termination.
  - success: page live at `/terms`, renders full text in the visitor's culture (cookie-based en/vi).
- **CAP-2** `/privacy` page, vi + en.
  - intent: data collected (files, IP, email — minimal), retention (plan-fixed delivery expiry), deletion rights, zero-knowledge scope stated honestly (browser-encrypted content the server cannot read vs metadata/IP/email/payment events it can), cookies used (culture, auth, ad-consent).
  - success: page live at `/privacy`, same culture behavior, includes VN Decree 13/2023 consent wording.
- **CAP-3** Footer legal links.
  - intent: crawlable `/terms` + `/privacy` links in the shared footer on every page.
  - success: links visible in `MainLayout` footer.
- **CAP-4** Dated + versioned documents.
  - intent: each page states version and last-updated date so a later (lawyer-reviewed) revision can supersede cleanly.
  - success: version + date line rendered on both pages.

## Constraints

- Legal prose is culture-switched markup blocks in the `.razor` page (vi/en via `CurrentUICulture`), not resx — page-length prose does not fit per-string resources; matches the tools-landing precedent.
- Pages are fully static (no DB/async) → the CLAUDE.md HTMX lazy-load exception applies; no skeleton needed.
- Visual system follows `DESIGN.md` (db-* classes, typography scale).
- Zero-knowledge honesty rule: never claim the server cannot see what it can (filenames, sizes, IP, email, payment events are visible; share/paste content encrypted client-side is not).
- No new cookie-consent banner: the ads consent endpoint already exists; privacy page documents cookies instead.

## Non-goals

- DMCA/abuse-report page (#53), account-deletion implementation (#59), analytics rollout (#24), lawyer review of the text (assumption above), production-jurisdiction re-review.

## Success signal

`/terms` and `/privacy` are reachable in vi and en, cover deletion / DMCA contact / payments-refund / zero-knowledge scope, footer links present on every page, build and tests green.
