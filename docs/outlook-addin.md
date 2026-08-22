# The Outlook add-in (#194)

Design and invariants for the task pane that turns a mail attachment into a link.

Related: [`developer-platform.md`](developer-platform.md) (the API key it authenticates with).

## The gesture

Somebody is writing a mail with a 25 MB attachment. One button replaces the attachment with a
QuickShare link, using the account's existing expiry and limits. Nothing else is configurable
from the pane: it is a column the width of a phone inside somebody else's window, and it is the
wrong place to re-litigate settings that already have a home.

Compose surface only. A read-pane button ("save somebody else's attachment") is a different
feature, and declaring a button that does the wrong thing is worse than not having one.

## The manifest is served, not shipped

Every URL in an Office manifest is absolute and must match the deployment, so it is generated
from the same `Email:BaseUrl` every other absolute link uses. A manifest carrying another
hostname installs cleanly and then silently does nothing.

The add-in id is fixed. Outlook keys the installed add-in on it: changing it is a duplicate
install, not an update.

## It authenticates with an API key

The pane runs in a third-party webview where our cookie does not reliably travel. It uses an API
key (#115) — pasted once, kept in that webview's `localStorage`, revocable from the developer
screen like any other. That is the reason API-key authentication (batch 26) had to exist first:
before it, no credential this pane could hold was accepted by anything.

The upload is the same three steps the web upload takes — `start-r2`, PUT each presigned part,
`commit-r2` — so there is no add-in-specific ingest path to keep in step.

**Attachments are removed only after their link exists.** Removing first and failing after would
lose the file from a mail nobody has sent yet.

## One scoped CSP exception

Office serves `office.js` from its own CDN and renders the pane inside its window, so `/addins`
answers with a policy that allows that script source and names the Office hosts in
`frame-ancestors`. Scoped to that path: `frame-ancestors 'none'` everywhere else is what stops
the rest of the app being clickjacked, and widening it globally to serve one page would be a
trade nobody asked for.

## Unverified, and saying so

What is tested is what we serve: the manifest's shape and URLs, the pane rendering outside the
app shell, and the CSP exception being exactly one path wide. **Whether Outlook then behaves as
intended is a claim only a real Outlook can settle** — there is no Office host in CI. Treat the
pane as unverified against a live client until somebody sideloads the manifest and tries it.

## Not shipped

- AppSource listing and the Entra app registration guide for org-wide deployment: a submission
  process, not code.
- Per-attachment choice. All attachments on the message are converted; picking one is a list UI
  in a pane that has room for a button.
- Reading the account's brand or folder defaults into the pane — they apply server-side already,
  and showing them would mean a settings screen inside a sidebar.
