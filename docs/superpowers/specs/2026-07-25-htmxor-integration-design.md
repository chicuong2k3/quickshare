# Phase 3: Htmxor Integration Design

## Context

TeleCloud uses Blazor Static SSR with Htmxor. hx-boost is active on `<body>`, making all links AJAX. The API layer uses FastEndpoints returning JSON. This phase adds targeted HTMX attribute integration to replace client-side fetch/XHR calls.

## Scope

Only convert interactions where HTMX provides a clear win over the current approach. Keep existing JS where the interaction is complex or multi-step.

### Convert: Account profile form (hx-post)

The `/account/update` endpoint currently returns JSON `{ redirect }` forcing full page navigation. Instead, return the rendered profile card as HTML. Form gets `hx-post="/account/update" hx-target="#account-app" hx-swap="outerHTML"`. Remove the XHR-based `submitAccountForm()` JS function.

### Convert: Files search debounce (hx-trigger)

The search `<form method="get" action="/files">` already works via hx-boost. Add `hx-trigger="input changed delay:500ms"` for live search as the user types. Add `hx-target="#files-list"` to swap only the file list, not the full page. Wrap the file list section in `<div id="files-list">`.

### Keep: Folder CRUD (Alpine.js modals)

Create/rename/delete folder uses Alpine.js modals with multi-step UX, drag-and-drop, and toast notifications. These work well. No change.

### Keep: Download page (vanilla JS app)

The download page has a JS state machine managing password -> payment -> ad -> file phases, plus encryption, branding, reports. Converting only the password step to HTMX would split the state machine into two incompatible systems. No change.

### Keep: Pricing billing toggle (client JS)

The monthly/yearly toggle is pure client-side DOM show/hide. No server round-trip needed. No change.

## Implementation

### 1. Account form

Modify the `/account/update` handler in Program.cs to return HTML on success instead of JSON redirect:
- On success: return the full `#account-app` div as HTML (the outerHTML swap replaces the entire app container)
- On error: return the card with error message rendered
- Convert the existing standalone button+inputs into a proper `<form hx-post="/account/update" hx-target="#account-app" hx-swap="outerHTML" hx-encoding="multipart/form-data">` — HTMX needs `hx-encoding="multipart/form-data"` for the avatar file upload
- Change the Save button to `<button type="submit">` and remove `submitAccountForm()` JS
- Keep the branding manager JS as-is

### 2. Search debounce

In Files.razor:
- Wrap the file listing section in `<div id="files-list">`
- Add to the search form: `hx-trigger="input changed delay:500ms" hx-target="#files-list"`
- Keep the existing form structure otherwise