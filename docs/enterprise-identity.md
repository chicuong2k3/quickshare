# Enterprise identity

Authenticator enrolment (`#128`), Google Workspace / Entra ID SSO (`#181`), SCIM 2.0
provisioning (`#146`) and the workspace MFA policy that ties them together (`#219`).

These four are one subsystem because they answer one question in four places: *who is this, and
does the organisation still say they belong here?*

---

## The rule everything else follows

**A directory claim is proof of membership. An email domain is not.**

Anyone can hold a consumer Google account whose address ends in `@acme.com` — buying the domain
and pointing it at Gmail takes an afternoon. Only Google can put `hd=acme.com` in an ID token,
and only Entra can put its own `tid` there.

So the resolution order in `SsoLoginService.ResolveAsync` is fixed and not negotiable:

1. Read the directory claim (`hd` for Google, `tid` for Entra). No claim → no SSO, sign in as a
   consumer account exactly as before.
2. Find the workspace bound to that directory.
3. *Then* apply `AllowedEmailDomains` as a narrowing filter.

Getting steps 1 and 3 the wrong way round is an account-takeover bug: the workspace would hand a
seat to anybody who registered a convincing address. `EmailMatchesDirectory` is deliberately
named as a filter, never as a check, so a future reader does not mistake it for the gate.

**A directory belongs to exactly one workspace.** `UpdateSsoConfigEndpoint` refuses a directory
another workspace already claims (`sso_directory_taken`). Without it, any workspace could bind
itself to `acme.com` and quietly absorb every `acme.com` account that signed in afterwards —
the claim would be correctly proved and then handed to the wrong tenant.

## Sign-in

The consumer path is untouched. `FinalizeOAuthAsync` gained one call: after the account is
registered, `SsoLoginService.ApplyAsync` decides whether this ticket also earns a seat. It is
best-effort and swallows its own failures, because a misconfigured workspace binding must not be
able to take sign-in down for the whole deployment.

Google's handler does not map the `hd` claim, so `Program.cs` captures it in `OnCreatingTicket`.
Without that line there is no proof of directory membership at all — only an address.

Entra is registered as OIDC and only when configured, against a **tenant-specific authority**
(`login.microsoftonline.com/{tenant}/v2.0`). The `/common` endpoint would let any Entra tenant on
earth complete the handshake and leave the `tid` comparison as the only thing between them and a
workspace binding. One check is not a security boundary.

### JIT provisioning

`JitRole` null switches it off: the directory proves who somebody is, and an admin may still want
to decide whether they belong in this workspace.

- **Owner is never assignable.** It carries the bill. `#252`'s transfer flow exists because that
  move needs consent, and a directory group is not consent.
- **Seats are finite.** A directory account that qualifies when the workspace is full is refused
  and audited (`SsoProvisionRefused`) rather than overselling the seat pool `#106` bills on.
- **A revoked seat coming back through the directory is a rehire**, not a bypass. The directory is
  the system of record for who works there.
- **Entra group → Admin** works via the `groups` claim. Google emits no group membership in an ID
  token, so a Workspace binding always provisions at `JitRole`. That is a provider limitation, not
  an omission.

## SCIM 2.0 (`#146`)

`/scim/v2/Users`, bearer-token auth, one token per workspace at a time.

**The token is the tenant boundary.** SCIM has no cookie and no signed-in user, so authorisation
is the token and nothing else. Every query is scoped to the workspace it resolves to — never by
resource id alone (`#190`). `ScimMapper.FindAsync` takes the workspace id for exactly this reason.

The endpoint the feature exists for is `PATCH .../{id}` with `active: false`. When IT deactivates
somebody in Okta or Entra, the seat must die: revoked, the borrowed plan restored, the event
audited. A PATCH that returns 200 and leaves the seat live is the precise failure SCIM is bought
to prevent.

Two shapes of that PATCH are read, because providers disagree:

```jsonc
{ "Operations": [ { "op": "replace", "path": "active", "value": false } ] }   // Okta
{ "Operations": [ { "op": "Replace", "value": { "active": false } } ] }       // Entra
```

Reading only one would leave half the market silently un-offboarded — worse than not shipping
SCIM, because the compliance answer would be wrong rather than absent.

Other decisions worth knowing:

- **Idempotent on `userName`.** A re-run sync gets the existing seat back with 200. Creating a
  second seat for one person would silently eat a paid seat.
- **`DELETE` is deactivation, not deletion.** The membership row is the audit trail (`#145`); a
  provider removing an assignment has said "no longer has access", not "erase the record".
- **A seat can exist before its person.** That is the pending-invite row `#106` already models.
  SCIM fills it in; the first SSO sign-in claims it.
- **Unsupported filters are ignored, not rejected.** Only `userName eq "…"` is parsed. A
  half-understood filter that silently drops rows is worse: the provider reads an empty list as
  "not provisioned" and creates a duplicate.
- **The owner's seat is never deactivated by SCIM.** It carries the subscription, and closing it
  would leave a paid workspace with nobody able to administer it.

## Authenticator (`#128`)

RFC 6238, computed with the BCL. No dependency — the algorithm is an HMAC over a counter and a
modulo, and it has not changed since 2011. `HMACSHA1` is what the RFC specifies and what every
authenticator app implements; it is not affected by the collision attacks that retired bare
SHA-1. The CA5350 suppression says so at the call site.

Because sign-in here is OAuth-only, this is a genuine second factor rather than a password's
sidekick: the first factor is the identity provider, the second is a device.

Two replay defences, both trivially wrong in ways a happy-path test would never notice, both
covered by their own test:

- **`LastUsedStep`.** A code is single-use. Without it, one shoulder-surfed six digits stays valid
  for the rest of its thirty-second window.
- **Recovery codes are spent on use.** A code that let somebody in once must not do it again.

Drift is ±1 step. Enrolment is not complete until a code verifies — enabling on the strength of
"we showed them a QR code" is how an account ends up with a second factor nobody can produce.

The secret is returned exactly once, by `POST /api/account/totp/start`, and is never readable
afterwards. The enrolment UI fetches and renders it client-side rather than server-rendering it,
so it never enters the page source, the browser cache, or a proxy log.

## MFA policy (`#219`)

`MfaEnforcementMiddleware` challenges for two separate reasons, and they are not the same thing:

1. **The account has an authenticator** → every session must answer it. An enrolled account whose
   sessions sail past the challenge has a decoration.
2. **A workspace policy requires one and the account has not enrolled** → inside the grace window
   the request goes through with a nudge on the security page; past it, the account is held out.

The grace window runs from **the later of** the policy starting and the member joining. Measuring
only from the policy would lock out every future hire the instant they accept an invite.

**SSO members are exempt.** Their directory already enforces a second factor, and requiring
another would be theatre the directory admin cannot switch off. They count as covered in the
posture number a SOC 2 reviewer reads.

Two things this gets right that are easy to get wrong:

- **The allowlist must contain everything needed to *satisfy* the challenge and nothing else.**
  Too generous and the product is open; too strict and the only way to enrol is behind the wall
  that enrolment removes. `Totp_EnrolmentRouteStaysOpenToAChallengedSession` pins that.
- **Removal is refused while a policy requires it** (`mfa_policy_locked`), even with a correct
  code. Otherwise the policy is a suggestion.

Enrolment is cached for a minute because the middleware runs on every authenticated request, and
without it the check is a database round trip per page load for the majority of accounts that
have no authenticator at all. Enrolment and removal drop the key immediately.

## Configuration

```jsonc
"Authentication": {
  "Entra": {
    "TenantId": "…",      // all three required, or Entra is simply not registered
    "ClientId": "…",
    "ClientSecret": "…"
  }
}
```

Google Workspace needs no extra configuration — it rides the existing Google OAuth registration;
what makes it enterprise is the `hd` claim and the workspace binding.

## Screens

| Route | Who | What |
|---|---|---|
| `/account/security` | any account | Enrol, recovery codes, remove, current policy state |
| `/mfa` | a held session | The challenge |
| `/account/workspace/sso` | workspace admins | Directory binding, MFA policy and posture, SCIM tokens |

All three are `noindex`. `/mfa` is deliberately **not** HTMX lazy-loaded — it is the wall a held
session lands on, and a skeleton that fills in a moment later reads as "the site is broken",
which is how somebody files a ticket instead of typing six digits.

## Invariants for anyone changing this

- **Never establish directory membership from an email address.** The claim comes first, always.
- **Never widen the Entra authority to `/common`.**
- **Never return a secret twice** — not the TOTP secret, not a SCIM token. Both are shown once at
  creation and stored as hash (SCIM) or as a value no endpoint reads back (TOTP).
- **Scope every SCIM query by the token's workspace**, never by resource id alone.
- **New MFA-exempt route?** Ask whether it can *satisfy* the challenge. If not, it does not belong
  in the allowlist.
- **New provisioning path?** It must revoke as well as create. Half a lifecycle is the thing
  procurement is asking about.

## Not shipped

- **SAML 2.0 (`#139`).** Wants a signing library, certificate handling and IdP metadata parsing —
  a batch of its own rather than a corner of this one. OIDC covers Google Workspace and Entra,
  which is what mid-market procurement asks for first; SAML is what Okta-standardised enterprises
  ask for, and they will ask for it properly.
- **SCIM `/Groups`.** Role mapping runs off the Entra `groups` claim at sign-in. A second
  authority for the same fact is a synchronisation problem nobody has yet asked for.
- **Session revocation on deprovision.** The seat dies immediately and every workspace operation
  re-checks it, so a deprovisioned member loses workspace access at once — but a cookie for their
  personal account stays valid until it expires. Cutting that needs a session store (`#105`).
- **Per-user MFA exemptions.** `#219` asks for none, and an exemption list is how a policy quietly
  becomes advisory.
- **TOTP enrolment before first sign-in.** The second factor attaches to an account; there is no
  account to attach it to until the first factor has been used once.

## Tests

`tests/QuickShare.Server.Tests/E2E/EnterpriseIdentityTests.cs` (32).

The TOTP implementation is anchored to the **RFC 6238 reference vector** rather than to itself: a
broken implementation still round-trips its own codes, so self-consistency proves nothing about
whether a real authenticator app can produce a code this accepts.

The rest cluster on proof and revocation — a directory claim being required, a lookalike domain
being refused, a replayed code being refused, a deprovision actually revoking, one workspace's
SCIM token failing to reach another's seat, and SSO-only refusing to lock the owner out.

The test harness rebuilds its principal from headers and never round-trips a cookie, so the
cookie **re-issue** in `MfaSession.MarkVerifiedAsync` is out of its reach; `X-Test-Mfa` stands in
for its result. These tests cover the gate's decisions, not the cookie mechanics — worth knowing
before trusting them with a change to the latter.
