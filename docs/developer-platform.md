# Developer platform

API keys (`#115`) and outbound webhooks (`#39`), with signing and retry (`#172`), an event log
(`#223`) and replay (`#288`). One subsystem, because a webhook nobody can authenticate against
and a key with nothing to call are each half a feature.

---

## API keys

`qs_` + 256 bits, base64url. Stored as SHA-256; **returned exactly once**, by the response that
creates it. There is no endpoint that can read one back, and that is deliberate: a credential a
support agent can retrieve is a credential an attacker can social-engineer.

What is stored alongside the hash is a 12-character **prefix**, shown in the UI. That exists so
somebody staring at a leaked key in a log can tell *which* of their keys to revoke without the
displayed value being usable. Identification without capability.

Accepted as `Authorization: Bearer <key>` or `X-Api-Key` — the second because half the world
reaches for that header first, and refusing it produces a support ticket rather than security.

Scopes are coarse on purpose — `read`, `write`, `manage`. Fine-grained scopes look thorough and
become a list nobody reads, ticked wholesale. The distinction that survives contact with reality
is "can this key change things, or only look at them?"

Expiry is optional and is the cheapest limit on a key that leaks. Revocation bites immediately;
there is a test that says so, because a revoke button that takes effect on the next cache refresh
is not a revoke button.

## Webhooks

Three events today: `delivery.created`, `delivery.downloaded`, `delivery.expired`. HTTPS only — a
signed payload sent over plaintext is a payload anybody on the path can read, and these carry file
names.

### Signing (`#172`)

```
X-QuickShare-Signature: HMAC-SHA256(secret, "{timestamp}.{body}")
X-QuickShare-Timestamp: 1700000000
X-QuickShare-Event:     delivery.created
X-QuickShare-Delivery:  <delivery id, stable across retries>
```

**The timestamp is inside the signed material, not beside it.** Sign only the body and a captured
request stays valid forever, with no way for the receiver to tell a replay from the original.
Receivers should reject a timestamp outside a few minutes of now.

Same construction as the SIEM sink (`#191`), so there is one shape to document and one to get
right.

### Queued, never inline

The Slack/Teams notifier (`#180`) posts inline and documents why — a share must not fail because
a chat workspace is down. That only holds because a chat post is best-effort and forgettable.

A webhook is an integration contract. It has to survive the receiver being down, which means a
row, a retry schedule and a job — not a fire-and-forget call on the request thread.

### Retry and parking

Exponential backoff — 1m, 2m, 4m, 8m, 16m, 32m — then the delivery is marked failed **and kept**.
The usual cause of a failed webhook is the receiver being down or rate-limiting, and hammering it
makes both worse.

After 20 consecutive failures the endpoint **parks**: queued events stop being attempted and stay
pending. Retrying a dead URL forever fills a queue with work that will never succeed; parking
makes the failure visible instead. One success clears the streak, and saving the endpoint clears
it too — fixing your receiver and pressing save is the obvious gesture, so a separate "resume"
control would be ceremony. (Same reasoning as the channel hook.)

The job takes a bounded slice per pass. That bound matters: one receiver timing out at ten seconds
must not hold the whole queue behind it, so a slow endpoint delays its own events rather than
everybody's.

### The log and replay (`#223`, `#288`)

Every attempt is a row: status, attempt count, response code, and a truncated response body for
diagnosis. Truncated because an endpoint that returns a megabyte of HTML on error should not be
able to fill the database with it.

Replay **re-sends the original payload** and writes a **new row**. Both halves are deliberate:

- Regenerating the payload would send today's view of the delivery, which is not what failed and
  not what is being debugged — and for an expired or deleted share there would be nothing to
  regenerate from.
- A new row rather than a reset, because the log records what happened. Rewriting history to hide
  a failure is exactly what a debugging log must not do.

The test console (`POST .../test`) goes through the same queue and the same signing as a real
event. A special-cased test path would prove only that the test path works.

## Privacy mode interacts with this

`PrivacyMode` (`#187`) suppresses the `delivery.downloaded` webhook along with the analytics event
and the download record. A share that keeps no record must not export one instead — the same rule
that stopped privacy mode opening a comment thread in `#289`.

## Retention

Completed deliveries are purged after 30 days by `WebhookDeliveryJob`. Long enough to debug an
integration, short enough that the log is not a second copy of the delivery history.

## Invariants for anyone changing this

- **Never return a key or a webhook secret twice.** Both exist in exactly one response, and there
  is a test asserting neither appears in the rendered page.
- **Never sign a body without a timestamp.**
- **Never send a webhook inline on the request path.** Queue it.
- **Never rewrite a delivery row to hide a failure.** Add a row.
- **Any new event must respect `PrivacyMode`.**
- **Scope every query by the calling account**, never by resource id alone.

## Not shipped

- **Per-scope enforcement on existing endpoints.** Keys carry scopes and `ApiScopes.Grants` reads
  them, but the existing endpoints still authenticate by cookie — wiring key-based auth through
  every one of them is a larger and separate change, and claiming otherwise would be the exact
  "endpoint nobody can reach" defect in reverse. Today a key identifies its owner; the surface it
  unlocks grows with the API itself.
- **Secret rotation without re-creating the endpoint.** Delete and re-add works and is honest;
  rotation with an overlap window is a real feature and deserves its own issue.
- **Per-key usage analytics** (`#273`) — `LastUsedAt` exists, a dashboard does not.
- **API versioning** (`#199`) and an interactive playground (`#185`).
- **More events.** Comment posted, approval decided and workspace membership changes are all
  plausible; three events that people actually asked for beats twelve that guess.

## Tests

`tests/QuickShare.Server.Tests/E2E/DeveloperPlatformTests.cs` (20).

The signature test is the load-bearing one: it asserts that the same body at two different times
produces two different signatures, which is the property that makes replay detectable at the
receiver. The retry test drives the job through its whole attempt budget against a hostname that
does not resolve, then checks the row survives — because the row is what replay sends.

## API keys actually authenticate (found in batch 26)

For six batches the keys authenticated **nothing**. They were issued, scoped, listed, revocable
and documented, and every endpoint read the auth cookie — so a `write` key could not create an
upload and a `read` key could not read a delivery. The scopes on the developer screen described
permissions nothing ever consulted. The first thing that accepted a key was the WebDAV mount
(#195), which is how it surfaced.

`ApiKeyAuthenticationMiddleware` closes it. Middleware rather than an authentication scheme, so
every existing endpoint keeps working untouched: the principal it builds carries the same
provider claims a cookie does, and `GetUserOrDefaultAsync` resolves the same account by the same
route.

Rules that hold:

- **Only `/api/`.** The mounts and the S3 gateway authenticate themselves, and pages are for
  people.
- **A cookie wins.** A browser session that also carries a key is still a person; downgrading it
  to the key's scopes would be a surprise in the wrong direction.
- **Scope by method**: GET/HEAD/OPTIONS need `read`, DELETE needs `manage`, everything else needs
  `write`. Coarse, matching how the scopes are described. A per-endpoint permission table would
  look thorough and drift the first time somebody adds a route without remembering it exists.
- **Second-factor-verified by construction.** A key *is* an independent credential, cannot be
  phished out of a session, and is revocable in one click — and a machine caller has no way to
  answer a TOTP prompt. Without this, enrolling an authenticator would silently kill every
  integration the account has.
- **It sits between `UseAuthentication` and `UseAuthorization`**, because it *is* authentication:
  authorization evaluates the endpoint's requirements immediately after, and a principal built
  any later arrives to a 401 that was already decided.
