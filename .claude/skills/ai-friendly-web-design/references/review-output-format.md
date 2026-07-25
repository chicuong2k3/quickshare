# Review Output Format

Use this format when the primary task mode is `Review Existing UI`.

When reviewing, label findings with these severity levels:

- `🔴 High`
- `🟡 Medium`
- `🟢 Low`

Use the text label exactly even if emoji rendering is unavailable.

## Summary

- 🔴 High:
- 🟡 Medium:
- 🟢 Low:

## Findings

| Severity | File | Issue | Why it matters | Suggested fix |
|---|---|---|---|---|

Rules:

- List High findings first.
- Every finding must explain the impact on accessibility, automation, agent operability, or reproducibility.
- Suggested fixes must be specific enough for an engineer to implement without guessing the intent.
- Do not include vague style-only observations unless they affect task completion or state clarity.

## Patch Plan

1.
2.
3.

Use this section only when a concrete remediation plan would help sequence the work.

## Test Suggestions

- Cover the critical user path.
- Check loading, error, empty, and success states when they exist.
- Prefer role, label, and text locators.
- Use custom stable locators only when needed.

## AI-Readable Content Notes

Use these notes when the UI includes content-heavy or machine-consumed surfaces.

- Prefer a hidden summary, `sr-only` summary, or `aria-describedby`-linked summary by default.
- Only make a summary visibly present if the user explicitly wants visible text or it does not affect the existing layout.
- Public content pages should have clear headings.
- Structured data can help when the page is meant to be publicly interpreted by machines.
- Do not expose private or internal information just to make the page more machine-readable.
- Do not recommend agent-specific metadata unless the product already needs it or the user explicitly asks for it.
