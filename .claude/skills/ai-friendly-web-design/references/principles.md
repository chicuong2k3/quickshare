# Principles

## Core Goal

An AI-friendly web interface should be:

1. Discoverable from the DOM
2. Operable through native browser events
3. Understandable through readable text and state
4. Reproducible through URL or explicit state
5. Testable through stable locators
6. Accessible to assistive technologies

These are the decision rules behind the rest of this skill. If a UI choice improves visual polish but weakens discoverability, operability, state clarity, reproducibility, or testability, treat that as a design cost that must be justified.

## Core Principles

### Prefer explicit structure over visual implication

Important structure should exist in the DOM, not only in layout or styling. Headings, lists, tables, forms, navigation regions, status messages, and actionable controls should be represented with the right HTML primitives so both people and tools can understand the page shape.

### Prefer native browser behavior over simulated behavior

Native controls expose stable keyboard, focus, value, disabled, and submission behavior. Every custom replacement increases the burden on implementation, testing, and accessibility review. Use custom widgets only when the product need is real and the fallback behavior is still operable.

### Make critical state readable

Loading, error, empty, success, validation, and permission states should be available as readable text or a programmatically associated message. Color, animation, and transient toast feedback are not enough for critical flows.

### Keep flows reproducible

A user or test should be able to return to a meaningful state without guessing hidden client state. Search, filters, sorting, pagination, selected tab, and view mode usually belong in the URL unless doing so would expose sensitive data.

### Keep automation stable but sparse

Stable locators are valuable for critical controls and status, not every node. Prefer user-facing locators first. Add custom locator hooks only when semantics, labels, or text are not enough to make the interaction robust.

### Optimize for task completion, not just compliance

The main question is whether a human, screen reader, browser automation tool, or AI agent can complete the task reliably. A technically valid but brittle flow is still a problem if it blocks or confuses critical work.

## Severity Model

Use this severity model during review.

### High

A High issue blocks a critical user task or makes the task effectively inaccessible or inoperable.

Examples:

- A critical action is unreachable or undiscoverable
- A form field or button has no accessible name
- A critical error reason is not exposed as readable text
- A critical flow depends on hover-only interaction
- Login, payment, or authorization depends on popup-only or new-tab-only behavior
- A required workflow depends on drag-only or canvas-only interaction without an equivalent path

### Medium

A Medium issue makes a task brittle, ambiguous, or hard to reproduce, but does not fully block the task.

Examples:

- A critical action or field lacks a stable locator when semantics are not enough
- Search, filter, sort, or pagination state is hidden from the URL without a clear reason
- Loading state is not readable or does not indicate progress
- A disabled action has no explanation
- Validation exists visually but is hard to associate with the field programmatically
- Important state is only communicated in a transient toast

### Low

A Low issue improves clarity, consistency, or extraction quality, but is not likely to block a critical task on its own.

Examples:

- Decorative SVG or layout chrome is not hidden from assistive tech
- Heading hierarchy is noisy or inconsistent
- Locator naming is inconsistent across similar controls
- Minor content structure makes automation or extraction less clean than it should be

## Review Questions

Use these questions to guide triage:

1. Can a user discover the key task from the DOM and text alone?
2. Can they operate it with standard browser interaction?
3. Are state changes readable and attributable?
4. Can the same state be reproduced or shared?
5. Can tests and tools locate the important controls reliably?
6. If something fails, is the reason obvious and actionable?
7. Would this change alter an existing hover, click, close, expand, or keyboard path?
8. If the new semantics, summary text, or locator hooks were removed, would the original UI behavior and visible layout remain unchanged?

If the answer to any of the first three questions is no on a critical path, expect a High finding. If the issue mainly affects reproducibility or locator stability, expect Medium unless it also blocks task completion.
If the answer to either of the last two questions is no, treat the fix as a behavior or layout change and require confirmation before implementation.
