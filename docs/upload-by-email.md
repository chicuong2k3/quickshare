# Upload by email (#104)

Design and invariants for forwarding a file in and getting a link back.

## The gesture

An attachment arrives on a phone, in a mail client, in a context where opening the app and
re-uploading is three minutes of fighting a file picker. Forwarding the mail to a private address
and getting links back is the whole feature.

## What makes it safe

**Not secrecy.** An ingest address travels in the headers of every mail it ever appears in, gets
quoted in replies, and ends up in somebody's autocomplete. Treating it as a secret would be
building on sand.

What makes it safe is that **only the account's own address may use it**. Anybody who learns the
address can send to it; nobody but the owner gets a delivery out of it, and nobody but the owner
is ever told anything. Mail `From` is forgeable — which is why this is a convenience boundary
rather than a security one, and why the worst a forged `From` achieves is a delivery in the
owner's own list, mailed to the owner's own address.

Accepting from anyone would turn a private address into an open upload endpoint charged to
somebody else's quota.

**Minted on first ask, not at signup.** An address nobody requested is a live ingest route nobody
watches. It is stable afterwards, because an address that changed would break every forwarding
rule set up against it.

**No address at all when the deployment cannot receive mail.** With no `Email:InboundDomain`
configured the endpoint reports the feature as unavailable rather than handing out an address no
mail server would accept.

## The webhook

`POST /api/uploads/inbound` takes the provider's parsed mail: `from`, `to`, `subject`, and
attachments as base64. Base64 rather than URLs on purpose — fetching from an address somebody
else controls is the SSRF surface `RemoteUrlFetcher` exists to contain, and it would make
ingestion depend on that link still being alive.

Authentication is a shared secret (`Email:InboundSecret`) compared in constant time; with none
configured, every call is refused. After that the endpoint answers 200 to almost everything: an
unknown address, a stranger's `From`, a mail with nothing attached. A provider retries anything
that is not a 200, and none of those are conditions a retry fixes.

## The same limits as any other upload

Plan file-size cap, monthly quota, retention clamp, storage region, expiry. A forwarded file is
an ordinary delivery with `Source = email`; nothing about arriving by mail relaxes a limit.

At most ten attachments per message — a mail is not a bulk uploader, and more than that in one
message is a mistake worth not honouring.

## Not shipped

- Inbound-mail *bodies* as text shares. `#178` covers pasted text and has a better interface for
  it than a forwarded signature block.
- Forwarding into a workspace folder. The mail carries no folder, and guessing from the subject
  line is the kind of cleverness that files somebody's contract in the wrong place.
- Per-account rules (auto-expiry, auto-password by subject tag): a second settings surface for a
  feature whose whole appeal is having none.
- DKIM/SPF verification of the forwarding address. The provider's parse result is trusted; the
  account boundary is what carries the safety here, not the envelope.
