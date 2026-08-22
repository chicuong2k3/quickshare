# Passwordless sign-in

Passkeys (`#68`) and emailed sign-in links (`#132`). Two more doors into an account that is still
created through an identity provider.

---

## Both are additional doors, not replacements

Every account here is created through OAuth or SAML. That single fact shapes the whole design:

- **Losing every passkey is not a lockout.** The provider that created the account still works. So
  removing the last passkey is allowed, and there is no separate passkey-recovery mechanism to
  build (see the note on `#118` below).
- **A magic link cannot create an account.** Requesting one for an unknown address does nothing.
  Otherwise a sign-in form doubles as a registration form anybody can point at somebody else's
  address.

## Passkeys

The cryptography is Fido2NetLib's. What is written here is what a library cannot decide: which
credential belongs to whom, how a challenge survives the two halves of a ceremony, and what a
failure is allowed to say.

**Challenges are a table, not a cache.** `CacheService` no-ops when caching is disabled, which
would silently turn a security control off from a config file — the same reasoning the SAML
assertion store follows. They are consumed on read: a challenge that survives its ceremony is a
replay waiting to be noticed by somebody else.

**Sign-in is usernameless.** No address is asked for, and no credential ids are returned. Both are
privacy decisions as much as UX ones: returning credential ids for an address would make the
endpoint an account-existence oracle.

**Every sign-in failure returns the same error.** The caller is anonymous, and the difference
between "no such credential" and "bad signature" is information about which credential ids exist.

**A passkey session starts second-factor verified.** A passkey is a possession factor with user
verification — it already satisfies what the MFA gate (`#219`) is asking for. A magic link does
not: it proves control of an inbox, which is a first factor.

**The signature counter** is stored and updated. Many authenticators legitimately report zero and
never move, so a zero counter is not treated as evidence of anything.

## Magic links

Single-use, 15 minutes, stored as a hash. The raw token exists in one email and nowhere else.

**Requesting a link always returns 204**, whether or not the address has an account — and the
email send is wrapped so a failure cannot change that. That wrapping is not defensive habit: a
test caught the leak. With email unconfigured, a known address threw a 500 and an unknown one
returned 204, which is exactly the oracle the rest of the method exists to prevent. A failure to
send is an operational problem and belongs in the log, not in a response a stranger can diff.

**Single-use matters more than it looks.** Corporate mail gateways follow links to scan them. One
of those must not be able to consume the human's only chance to sign in without them noticing —
so the second use fails rather than silently signing somebody in.

Rate-limited to 5 per address per hour. A mailbox is not a place to send unlimited mail.

## On `#118` (passkey recovery codes)

The issue asks for an escape hatch if passkeys are lost. **That premise does not hold here**, and
saying so is more useful than building a redundant mechanism:

- Every account has an identity provider behind it, which keeps working.
- Accounts with an authenticator already have recovery codes from `#128`.

So `#118` is left open with that written on it rather than closed with a hand-wave. If a future
change ever makes passkeys the *only* credential for some account, the escape hatch becomes real
work and the issue is where it belongs.

## Invariants for anyone changing this

- **Never let the response differ by whether an account exists.** Not the status, not the body,
  and not by throwing.
- **Consume a challenge before verifying it**, so a failed attempt cannot be retried against it.
- **A magic link is single-use.** Mark it used before doing anything else with it.
- **Any new sign-in path goes through `AuthSignIn.CompleteAsync`**, which starts the revocable
  session (`#105`). A path that calls `SignInAsync` directly issues a cookie nothing can revoke.

## Not shipped

- **Passkey as a step-up factor** for sensitive actions. It signs in; it does not yet re-confirm.
- **Conditional UI (autofill) passkeys.** Wants `navigator.credentials.get({mediation: 'conditional'})`
  on the login field and a different page flow; the button works everywhere today.
- **Attestation verification.** `AttestationConveyancePreference.None` — we do not care which
  authenticator model was used, and asking would mean maintaining a metadata service for a policy
  nobody has stated.
- **Magic link for a new address.** Sign-in only, deliberately.

## Tests

`tests/QuickShare.Server.Tests/E2E/PasskeyAndMagicLinkTests.cs` (14).

The WebAuthn ceremony needs a real authenticator, so the tests cover everything around it — which
is where the mistakes live. Challenge consumption, owner scoping, single-use tokens, expiry,
hashed storage, and the two endpoints answering identically for known and unknown addresses.
