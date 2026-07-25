# Locator And State

This reference defines how to make critical UI controls easier to locate and how to make important page state reproducible.

## Stable Locators

Prefer user-facing locators first.

Priority order:

1. role
2. label
3. text
4. alt text
5. placeholder, when appropriate
6. stable custom locator, when needed

Examples:

- `page.getByRole('button', { name: 'Save draft' })`
- `page.getByLabel('Email')`
- `page.getByText('No results found')`
- `page.getByAltText('Product photo')`

### When To Add Custom Locators

Add stable custom locators only when a critical element cannot be targeted robustly through user-facing semantics alone.

Typical targets:

- submit, save, confirm, and delete actions
- search entry points
- filter and sort controls
- pagination controls
- important form fields
- status messages that confirm or block task completion

### Naming Patterns

Recommended patterns:

- `data-ai-action="{domain}.{object}.{action}"`
- `data-testid="{page}.{component}.{element}"`

Good examples:

```html
<button data-ai-action="checkout.order.submit">Place order</button>
<input data-testid="settings.profile.display-name" />
```

Keep names stable, descriptive, and tied to user intent rather than implementation details.

### Avoid

- CSS class selectors
- random IDs
- index-based selectors
- visual position selectors
- database IDs used as selectors
- adding test IDs to every node by default

If the UI can already be found through role, label, or text, prefer that.

## URL State

Put user-meaningful, shareable state in the URL when reasonable.

Usually belongs in the URL:

- search query
- filters
- sort order
- pagination
- selected tab
- date range
- view mode

Usually does not belong in the URL:

- password
- token
- sensitive personal data
- transient loading state
- unsaved long draft content

### Why URL State Matters

- Reproducible bugs are easier to review and fix.
- Shared links preserve task context.
- Browser navigation works more predictably.
- Automation and AI agents can return to a known state without replaying the whole session.

### Practical Rules

- Use clear parameter names that match the UI language.
- Keep default values implicit when that makes URLs cleaner.
- When state is omitted from the URL, document the reason in code or review notes.
- Avoid encoding sensitive or high-churn internal state unless it materially improves the user flow.

## Review Checklist

1. Can a critical control be located through role, label, or text?
2. If not, is there a stable custom locator on the smallest useful target?
3. Are locator names tied to domain actions rather than styling?
4. Can the important page state be reproduced from the URL?
5. Is any sensitive or unstable state incorrectly exposed in the URL?
