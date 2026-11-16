# Sessions

Session management (`#105`), idle timeout and global sign-out (`#173`).

---

## Why the cookie was not enough

The auth cookie is self-contained: it proves who somebody is and expires on its own schedule, and
nothing on the server can take it back. That is fine right up until it is not — a stolen laptop,
an offboarded employee, a shared machine at a client site.

A "sign out everywhere" button over a self-contained cookie is decoration. So the cookie now
carries a session id and a row decides whether it still counts.

**The price is one indexed read per authenticated request.** That is the real cost of sessions you
can revoke, and it is deliberately not cached: caching the answer is precisely the thing that
would make revocation take effect *eventually* rather than on the next request.

`LastSeenAt` is written at most once every five minutes. Writing it per request would turn every
page load into a database write for a value nobody reads at that precision.

## Two different clocks

| | Asks | Value |
|---|---|---|
| Cookie lifetime | How long may somebody stay signed in? | 30 days, sliding |
| `IdleTimeout` | How long after they walked away? | 14 days |

The shared-machine case is why both exist.

## Legacy cookies are allowed through

A principal with no session claim validates successfully. Those are cookies issued before this
feature existed, and rejecting them would sign out every logged-in user on deploy in order to
enforce a rule none of them had a chance to satisfy. They expire on their own within 30 days, and
every new sign-in gets a session.

This is a deliberate, time-limited hole. It closes itself.

## Global sign-out keeps the current session

`RevokeAllAsync(userId, exceptSessionId)` — "sign out my other devices" is what people actually
want. Signing them out too turns a security action into a chore, and a chore is something people
skip.

Passing `null` revokes everything, which is the offboarding shape.

## SCIM deprovisioning now cuts sessions

When SCIM (`#146`) shipped, this was written down as a known gap: the seat died immediately and
every workspace operation re-checked it, but a signed-in browser kept working until its cookie
expired. That is not what "IT deactivated them" is supposed to mean.

`ScimPatchUserEndpoint.SetActiveAsync` now revokes every session of the deactivated member. There
is a test that drives the real SCIM PATCH and asserts the session stops validating.

## Invariants for anyone changing this

- **Never cache the validation result.** The moment it is cached, revocation is eventual.
- **Scope every revoke by user id**, never by session row id alone.
- **Keep the touch throttled.** A write per request is a write per page load.
- **A new sign-in path must start a session.** Both OAuth and SAML do; anything that calls
  `SignInAsync` directly and forgets will silently issue an unrevocable cookie.

## Not shipped

- **Per-session IP display.** The hash is stored (privacy `#26`), and showing a hash helps nobody.
  Geolocation is `#267`.
- **Naming a device.** `UserDevice` (`#149`, `#235`) already models the device; sessions model the
  sign-in. Merging them would conflate "this browser" with "this login".
- **Re-authentication for sensitive actions.** Removing an authenticator already requires a code
  (`#128`); a general step-up policy is its own feature.
- **Configurable idle timeout per workspace.** One value, documented, until somebody asks.

## Tests

`tests/QuickShare.Server.Tests/E2E/SessionManagementTests.cs` (11).

Mostly negatives, because the feature *is* the negatives: revoked, idle, unknown and
belonging-to-somebody-else all have to mean no. Plus the throttle (fresh session → no write; stale
→ write) and the SCIM path end to end.
