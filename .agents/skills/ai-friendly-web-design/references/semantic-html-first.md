# Semantic HTML First

Prefer native HTML before ARIA.

Native elements already carry semantics, keyboard behavior, focus behavior, and browser interoperability. ARIA should clarify or enhance behavior, not compensate for choosing the wrong element.

## Native Element Choices

Choose the simplest native element that matches the job.

| Use case | Prefer |
|---|---|
| action | `<button>` |
| navigation | `<a href="...">` |
| text input | `<input>` |
| long-form text | `<textarea>` |
| simple dropdown | `<select>` |
| checkbox | `<input type="checkbox">` |
| radio group | `<input type="radio">` |
| page structure | `<header>`, `<main>`, `<nav>`, `<footer>`, `<section>` |
| grouped form fields | `<fieldset>` and `<legend>` |
| tabular data | `<table>` with headers |

## Good Defaults

- Use a real `<button>` for actions, even when styling it as text or an icon.
- Use a real link for navigation that changes location.
- Associate labels with fields through `<label>` or explicit labelling attributes.
- Use headings to reflect page structure, not only typography.
- Expose validation and status messages in readable text near the relevant control.

## Good Example

```html
<form>
  <label for="email">Email</label>
  <input id="email" name="email" type="email" aria-describedby="email-error" />
  <p id="email-error">Enter a valid work email address.</p>

  <button type="submit">Create account</button>
</form>
```

## Bad Example

```html
<div class="field">
  <div class="label">Email</div>
  <div class="input-shell" contenteditable="true"></div>
  <div class="error-icon"></div>
</div>

<div class="submit">Go</div>
```

The bad example hides semantics, weakens keyboard behavior, and leaves both automation and assistive technology to infer intent from styling.

## Interaction Anti-Patterns

These patterns regularly break operability.

### `div` or `span` used as a button

Problem:

- Missing native button semantics and keyboard behavior
- Higher chance of broken disabled, focus, and pressed states

Preferred fix:

- Replace with `<button type="button">` or `<button type="submit">`

### Hover-only critical action

Problem:

- Important controls disappear for touch users, keyboard users, and automation

Preferred fix:

- Keep the action visible or expose a persistent alternative path

### Drag-only workflow

Problem:

- Hard to reproduce and often impossible for keyboard-only or automation users

Preferred fix:

- Offer buttons, menus, text inputs, or ordered controls as an equivalent path

### Popup-only login or payment

Problem:

- Causes context loss and breaks scripted flows

Preferred fix:

- Keep critical authentication and payment inside the main page context when possible

### Icon-only button without label

Problem:

- The action is ambiguous to screen readers, tests, and AI agents

Preferred fix:

- Add visible text or a reliable accessible name such as `aria-label`

### Fake select without keyboard support

Problem:

- Recreates a complex widget poorly and often breaks focus, selection, and announcement

Preferred fix:

- Use `<select>` for simple cases or implement the full interaction model deliberately when a custom widget is truly required

### Canvas-only information

Problem:

- Data becomes unreadable to assistive technology and extraction tools

Preferred fix:

- Provide a nearby text summary, data table, or list of key values

### Map-only information

Problem:

- Users cannot recover the content if the map is hard to inspect or manipulate

Preferred fix:

- Add a synchronized list, table, or address summary outside the map canvas

### Toast-only error

Problem:

- The failure reason disappears before users can act on it

Preferred fix:

- Keep the error message inline, persistent, and associated with the relevant step or field

### Color-only status

Problem:

- Meaning disappears for color-blind users, non-visual users, and automation

Preferred fix:

- Add text labels such as "Failed", "Pending", or "Paid"

### Disabled button without reason

Problem:

- Users know they are blocked but not how to recover

Preferred fix:

- Explain the blocking condition in readable text near the control

## ARIA Guidance

- Use ARIA to clarify intent when the native element alone is not enough.
- Do not replace a working native pattern with a more complex ARIA-heavy custom widget unless the product truly needs it.
- When using ARIA labels, make sure the label says what the control does, not what the icon looks like.
- When using ARIA relationships such as `aria-describedby`, verify the referenced content exists and is useful.

## Review Checklist

When evaluating implementation choices, ask:

1. Is this using the right native element for the job?
2. If not, is there a real product reason?
3. Is there an accessible name for each critical control?
4. Can the interaction be completed without hover, drag, or popups?
5. If the UI is visual-first, where is the equivalent text path?
