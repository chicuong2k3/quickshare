# Component Recipes

Use this file when the task needs quick implementation or review guidance for common UI components. These recipes are intentionally short. They are not a component encyclopedia.

These component recipes are examples of the general non-intrusive remediation rule, not the limit of where it applies.
In review, fix, and refactor work, preserve existing behavior and visible layout by default across all components, not only the ones listed here.

## Button

Use:

- Trigger an action with a native `<button>`.
- Set `type="button"` unless the button is meant to submit a form.
- Add a stable locator only for critical actions such as save, submit, confirm, or delete.

Required:

- Use a real `<button>`.
- Provide visible text or an `aria-label` for icon-only controls.
- Show readable loading text such as "Saving..." when pending.
- Prevent duplicate submission while pending with `disabled` or equivalent state.

Avoid:

- `<div onClick>` or `<span onClick>` for primary actions.
- Omitting `type` and accidentally submitting forms.
- Spinner-only loading state with no text.

Good:

```html
<button
  type="submit"
  data-ai-action="checkout.order.submit"
  disabled
  aria-busy="true"
>
  Saving order...
</button>
```

Bad:

```html
<div class="btn" onclick="saveOrder()">
  <span class="spinner"></span>
</div>
```

## Form Field

Use:

- Capture user input with a labeled native control.
- Connect help text and validation text through `aria-describedby`.
- Make required and invalid states explicit.

Required:

- Every input needs a label.
- Hint or help text should be readable and associated.
- Error text must be readable text, not color-only feedback.
- Mark invalid state clearly with HTML or ARIA state when needed.
- Mark required fields explicitly.

Avoid:

- Placeholder-only labels.
- Error icons without readable messages.
- Detached help text that is visually nearby but not associated.

Good:

```html
<label for="email">Work email</label>
<input
  id="email"
  name="email"
  type="email"
  required
  aria-invalid="true"
  aria-describedby="email-hint email-error"
/>
<p id="email-hint">Use your company address.</p>
<p id="email-error">Enter a valid work email address.</p>
```

Bad:

```html
<input placeholder="Work email" />
<span class="error-dot"></span>
```

## Modal/Dialog

Use:

- Present a focused secondary task in a dialog with a clear title.
- Move focus into the dialog when it opens.
- Return focus to the trigger after close.

Required:

- The dialog needs an accessible name.
- Initial focus must land inside the dialog.
- `Escape` should close the dialog unless the product intentionally forbids it.
- If the existing implementation already closes on outside click or blank-area click, preserve that path when adding dialog semantics.
- Background content must not be accidentally interactive while open.
- Preserve the existing close affordances and focus return behavior unless the user explicitly approves a behavior change.
- Critical flows should not depend on popup-only or modal-only behavior.

Avoid:

- Unlabelled overlays.
- Adding a second, library-default close button when the page already has a custom one.
- Opening a dialog without focus management.
- Closing the dialog and dropping focus on the document body.
- Replacing an existing working lightbox or modal with a generic dialog primitive solely to improve semantics.

Good:

```html
<button type="button" aria-haspopup="dialog">Delete project</button>

<dialog aria-labelledby="delete-title">
  <h2 id="delete-title">Delete project</h2>
  <p>This action cannot be undone.</p>
  <button type="button">Cancel</button>
  <button type="button" data-ai-action="project.delete.confirm">
    Confirm delete
  </button>
</dialog>
```

Bad:

```html
<div class="modal open">
  <div class="x">x</div>
  <div>Are you sure?</div>
</div>
```

## Dropdown/Menu

Use:

- Improve an existing menu trigger with additive semantics before considering any implementation replacement.
- Preserve current working trigger behavior when users already rely on it.
- Add stable locators only when user-facing locators are not enough.

Required:

- If an existing trigger already supports both hover and click, preserve both paths.
- Prefer adding `aria-haspopup`, `aria-expanded`, a clear label, and stable locators to the current trigger before changing the implementation.
- Keep current open and close behavior unless the user explicitly approves a behavior change.
- Treat "must not be hover-only" as a requirement to keep a non-hover path, not as a reason to remove hover when click already exists.

Avoid:

- Replacing a working hover-plus-click menu with a click-only popup.
- Replacing an existing menu with a focus-trapped popup primitive solely to add semantics.
- Removing a path users already rely on just because a new primitive has a different interaction model.

Good:

```html
<button
  type="button"
  aria-haspopup="menu"
  aria-expanded="false"
  data-ai-action="export.menu.toggle"
>
  Download
</button>
<ul role="menu" aria-label="Download options">
  <li role="menuitem"><button type="button">1X PNG</button></li>
  <li role="menuitem"><button type="button">2X PNG</button></li>
  <li role="menuitem"><button type="button">SVG</button></li>
</ul>
```

Bad:

```html
<button type="button" aria-haspopup="dialog">Download</button>
<div role="dialog">
  <button type="button">1X PNG</button>
  <button type="button">2X PNG</button>
  <button type="button">SVG</button>
</div>
```

## Toast/Banner

Use:

- Show lightweight feedback with a toast.
- Use a banner or inline message when the state must remain readable.
- Pair color with text labels.

Required:

- Errors must not exist only in auto-dismissing toasts.
- Critical success or failure state needs persistent readable text somewhere in the UI.
- Status must be understandable without color.
- Toasts should supplement the main state, not replace it.

Avoid:

- Auto-dismiss as the only error delivery path.
- Green or red color with no text meaning.
- Using toast history as the only audit trail of what happened.

Good:

```html
<div role="status" class="toast">Profile saved.</div>
<p class="success-summary">Your profile changes were saved successfully.</p>
```

Bad:

```html
<div class="toast error"></div>
```

## Search/Filter

Use:

- Combine search, filters, sorting, and result navigation in a reproducible flow.
- Reflect user-meaningful state in the URL.
- Provide a clear way to reset or clear active constraints.

Required:

- The search field needs a label.
- Search query, filters, sort, and pagination should usually be in the URL.
- Provide clear or reset controls.
- Loading, empty, and error states must be readable.
- Important state must not live only in React or local component state.

Avoid:

- Placeholder-only search inputs.
- Hidden filters with no visible current state.
- State that disappears on refresh because it never reached the URL.

Good:

```html
<label for="product-search">Search products</label>
<input id="product-search" name="q" type="search" />
<button type="reset">Clear filters</button>
<p>Showing results for "desk lamp"</p>
```

Bad:

```html
<input placeholder="Search..." />
<div onclick="clearAll()">x</div>
```

## Data Table

Use:

- Show structured row-and-column data with a real table.
- Give the table a caption or a nearby title that explains what it contains.
- Label row actions with task-specific text.

Required:

- Use `<th>` for headers.
- Provide a caption or clear table context.
- Sort state must be readable.
- Row actions need explicit labels.
- Loading, empty, and error states must be readable.
- Row actions must not appear only on hover.

Avoid:

- Grid-like `div` layouts for clearly tabular data.
- Sort icons with no text or announced state.
- Icon-only row actions with no accessible name.

Good:

```html
<table>
  <caption>Invoices</caption>
  <thead>
    <tr>
      <th scope="col" aria-sort="ascending">Date</th>
      <th scope="col">Amount</th>
      <th scope="col">Actions</th>
    </tr>
  </thead>
</table>
```

Bad:

```html
<div class="table">
  <div class="header">Date</div>
  <div class="row-action hidden-on-hover">...</div>
</div>
```

## Pagination

Use:

- Move through result pages with explicit navigation controls.
- Keep the current page readable and reproducible.
- Prefer pagination over infinite scroll for browse-heavy lists.

Required:

- The current page must be readable.
- Next and previous controls need clear labels.
- Page state should enter the URL.
- Disabled states must be explicit.
- Infinite scroll should not be the only browsing path for important result sets.

Avoid:

- Arrow-only controls with no text alternative.
- Pagination state that resets after refresh.
- Endless scrolling as the only way to reach older results.

Good:

```html
<nav aria-label="Pagination">
  <a href="?page=1" aria-current="page">1</a>
  <a href="?page=2">2</a>
  <button type="button" disabled>Previous page</button>
  <a href="?page=2">Next page</a>
</nav>
```

Bad:

```html
<div class="pager">
  <span><</span>
  <span>></span>
</div>
```

## Canvas/Map

Use:

- Show spatial or visual data with canvas or a map only when the visual surface is genuinely useful.
- Mirror critical information in text, list, or table form.
- Provide non-drag alternatives for key actions.

Required:

- Business-critical information inside canvas or map views needs text fallback.
- Important points should have a list or table alternative.
- Critical actions must not depend only on drag, pan, or zoom.
- Provide button, menu, or form alternatives for key interactions.
- Do not leave essential workflow state only inside the drawn surface.

Avoid:

- Canvas-only status dashboards.
- Map pins with no synchronized text list.
- Forced pinch, drag, or zoom as the only control path.

Good:

```html
<div id="delivery-map" aria-hidden="true"></div>
<h2>Delivery stops</h2>
<ul>
  <li>Stop 1: 21 River Street</li>
  <li>Stop 2: 8 Lake Avenue</li>
</ul>
<button type="button">Next stop</button>
```

Bad:

```html
<canvas id="route-planner"></canvas>
```
