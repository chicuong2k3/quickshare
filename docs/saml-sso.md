# SAML 2.0 SSO

Issue `#139`. Sits alongside the OIDC paths in `docs/enterprise-identity.md` — same seat
provisioning, different way of proving who somebody is.

Where OIDC proves the directory with a claim the provider signed into a token, SAML proves it
with a signature over an XML assertion. So the trust anchor moves: `DirectoryId` holds the IdP
entity id, and `SamlCertificate` is the key that has to have signed the assertion.

---

## Why this waited a batch

XML digital signatures have a long, well-documented history of implementations that validate *a*
signature rather than the signature over the element they then read — signature wrapping, comment
splicing, transform abuse. Every one of those bugs turns an SSO integration into an
authentication bypass, and none of them fail loudly.

So the signature validation, canonicalisation and assertion parsing are **ITfoxtec's**
(`ITfoxtec.Identity.Saml2` 4.20.1), not ours. What is written here is only what a library cannot
know: which workspace an assertion belongs to, whether its issuer is one we accept, and whether we
have seen it before.

### The dependency brought a finding

ITfoxtec depends transitively on `System.Security.Cryptography.Xml` **10.0.7**, which carries five
high-severity advisories — in XML signature handling, the exact code this feature trusts. The
NuGet audit failed the build over it, which was the right call.

It is suppressed in `Directory.Build.props` rather than pinned, because that assembly ships in the
.NET shared framework: NuGet **prunes** the package reference entirely (`NU1510` refuses an
explicit one), so 10.0.7 is never restored, never deployed and never loaded. What executes is
whatever the installed runtime provides.

**Which makes the mitigation a deployment obligation, not a csproj line: keep the .NET runtime
patched.** Re-check the suppression whenever ITfoxtec is upgraded — if the package ever stops
being prunable, the suppression stops being honest and has to become a pin.

## The endpoints

| Route | Auth | Purpose |
|---|---|---|
| `GET /saml/metadata` | anonymous | SP metadata to paste into the IdP |
| `GET /saml/login/{workspaceId}` | anonymous | SP-initiated sign-in → 302 to the IdP |
| `POST /saml/acs` | anonymous | Assertion consumer, SP- and IdP-initiated |

All three are anonymous by necessity: an IdP admin fetches metadata before any binding exists,
and the ACS receives a cross-origin form POST from a machine that holds no cookie. The ACS is
therefore exempt from antiforgery — which is correct, and is also why every check below matters.

## What the ACS actually does, in order

1. **Read the issuer as untrusted XML.** `SamlService.TryReadIssuer` decodes the base64 and pulls
   `/Response/Issuer` with a locked-down `XmlReader` — DTD prohibited, no resolver, no entity
   expansion, so the one place we touch untrusted XML ourselves cannot be turned into an XXE.

   This step is unavoidable and worth being explicit about: only the issuer can tell us which
   certificate the assertion must be signed by, and the issuer is inside the assertion. It is read
   *deliberately as raw XML rather than through the token handler*, because a handler read looks
   like validation without being one, and the next person editing this file must not be able to
   mistake it for one. The value is used for exactly one thing: selecting a binding. A forged
   issuer selects a certificate that will not verify the assertion.

2. **Find the workspace binding** for that issuer. No binding, or a half-filled one → 400.

3. **Validate.** `Unbind` checks the signature against the configured certificate, the audience
   against our entity id, the issuer against `AllowedIssuer`, and the assertion's own validity
   window. Every decision after this line rests on it; nothing before it is acted on.

4. **Check for replay.** See below.

5. **Read the email**, apply the `AllowedEmailDomains` narrowing filter — after the signature,
   never instead of it, the same order the OIDC path uses (`#181`).

6. **Register, provision, sign in.** Provisioning goes through `SsoLoginService.ApplySamlAsync`,
   which is the OIDC path's `ProvisionAsync` with the binding handed in rather than resolved.
   Seat limits, Owner immunity, admin-group mapping and rehire behaviour are all the same code —
   two provisioning implementations would drift and only one of them would get the next fix.

## Replay protection is a table, not a cache

A correctly signed assertion, still inside its validity window, replayed verbatim, is a stolen
one. The signature check says yes and the clock says yes; only a record of what has already been
consumed says no.

`SamlAssertionSeen` has a unique index on the assertion id, and consuming an assertion **is** the
insert — a replay is the insert failing. That is atomic across instances in a way a read-then-write
never is.

It is deliberately not the FusionCache: `CacheService` no-ops when caching is disabled, which
would silently turn a security control off from a config file, and a per-process cache would not
see a replay aimed at a second instance at all. Consumed rows are purged by
`SecurityEventCleanupJob` once past their window, at which point the time bound refuses them
anyway.

An assertion with no id is refused. Something untrackable cannot be trusted.

## Certificate handling

- **Self-signed is the normal case.** Okta and Entra both hand you one, so `CertificateValidationMode`
  is `None` and `RevocationMode` is `NoCheck`. The trust anchor is the exact certificate the admin
  pasted in — a stronger statement than "some CA vouched for it", and it does not make sign-in
  depend on reaching a CRL endpoint.
- **PEM armour optional.** Both forms come out of real admin consoles; refusing one produces a
  support ticket, not a safer deployment.
- **Validated at configuration time**, not at the first sign-in. A binding that cannot work is
  refused with `saml_certificate_invalid` while the admin is still looking at the screen.
- **Never returned.** The API reports a certificate as present, not what it is. It is a public
  key, but echoing configuration back invites diffing it instead of re-pasting from the IdP.

## NameID handling

An email claim is preferred, from any of the five type URIs Okta, Entra, Google and ADFS variously
emit. The NameID is used **only when it is itself an address**: a transient or persistent NameID is
an opaque pairwise pseudonym, and creating an account under one would mean an account identified by
a string its owner has never seen and cannot receive mail at.

## Session

A SAML session is issued the same claims as an OAuth one — everything downstream reads
`NameIdentifier` and the provider claim and must not care which front door was used — plus
`mfa_verified`. Federated sign-in already carried whatever second factor the directory required,
which is the same reasoning `#219` uses for its SSO exemption.

## RelayState

RelayState round-trips through the identity provider, so it is attacker-influenceable data that
comes back to us as a redirect target. Only a local path is honoured, and `//evil.example` is
rejected explicitly — it is a protocol-relative absolute URL that reads as a path if you only check
the first character.

## Not shipped

- **Single Logout (SLO).** Front-channel SLO is unreliable in practice (third-party cookies) and
  back-channel needs a session index store. Signing out here ends the local session; the IdP
  session is the IdP's.
- **Signed AuthnRequests.** Requires an SP signing key to manage and rotate. Every IdP tested
  accepts an unsigned request; the assertion coming back is what carries the security.
- **Encrypted assertions.** Same reason — an SP decryption key with a rotation story, for a
  channel already under TLS.
- **IdP metadata URL import.** The admin pastes three fields. Importing metadata means fetching a
  URL on a schedule and deciding what to do when it changes under you, which is a rotation feature
  rather than a parsing convenience.
- **Multiple bindings per workspace.** One workspace federates to one directory, same as `#181`.

## Invariants for anyone changing this

- **Never act on anything read before `Unbind`.** The pre-validation issuer read exists to select
  a certificate and for nothing else.
- **Never relax the `XmlReaderSettings`** in `TryReadIssuer`. DTD prohibited, resolver null.
- **Issuer and certificate are checked as a pair.** Either alone is decorative: a signature from a
  certificate we trust for another tenant proves nothing about this one.
- **Replay protection must stay durable and atomic.** If it ever becomes a cache again, it becomes
  a control that a config flag can switch off.
- **RelayState is never trusted as a URL.**

## Tests

`tests/QuickShare.Server.Tests/E2E/SamlSsoTests.cs` (12).

These mint a real identity provider — a self-signed certificate, a genuine signed
`Saml2AuthnResponse`, POSTed to the ACS the way Okta would. That matters more here than anywhere
else in the suite, because the thing under test is a signature check, and a signature check can
only be shown to work by handing it something that ought to fail.

Every negative test is the same shape: take a response that works, change one thing an attacker
controls, require a rejection.

| Changed | Expected |
|---|---|
| signed by a different key | 400 — otherwise the pasted certificate is decoration |
| issued by a different entity | 400 — otherwise one tenant's IdP signs into another's workspace |
| replayed verbatim | 400 — valid twice is stolen once |
| email outside the allowed domains | 400 |
| not a SAML response at all | 400 |

The positive test earns its keep by keeping the others honest: an ACS that rejected everything
would pass all five negatives.
