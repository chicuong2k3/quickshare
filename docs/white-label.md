# White-label and the preview watermark (#81, #155)

Design and invariants for the agency batch: a workspace's own hostname on the recipient page,
and a name drawn across in-page previews.

Related: [`workspace-branding.md`](workspace-branding.md) (the look),
[`workspaces-architecture.md`](workspaces-architecture.md) (seats, roles, audit).

## Why these two together

Both answer the same customer. An agency reselling QuickShare to its clients needs the delivery
page to look like theirs (#285/#286, shipped), to *live* at their address (#81, here), and needs
the work-in-progress they share not to leak by screenshot (#155, here). The first two are one
feature split across two batches; the third is the reason agencies asked for a preview at all.

`#81` composes rather than invents: sub-accounts are team seats (#106/#278), per-tenant API keys
are the developer platform (#115), billing is PayOS. What did not exist was the address.

The signed-in custom-domain settings page now renders the admin-scoped domain on direct SSR
GET and uses Unpoly to refresh the section after mutations. This does not change the verified-host
gate on public links/routes; native no-JS mutation forms remain pending.

## Custom domain (#81)

### The claim is not the grant

`workspace_domains` holds one row per workspace: the host, a reference token, and `VerifiedAt`.
Everything that reads a domain reads `VerifiedAt`, never `Host` alone. A workspace can therefore
type any hostname it likes and change nothing for anybody — including the hostname's real owner.

Two unique indexes carry the isolation: one on `WorkspaceId` (one address per brand; a second
address is a redirect, not a row) and one on `Host`. The second is the important one — it is the
same hole the SSO directory had in batch 12, where two workspaces both claimed `acme.com` and
whoever registered first absorbed the other's people.

### Verification is DNS resolution, not a TXT record

For the domain to work at all, the agency has to point it at us. So the check asks exactly that:
does `files.studio-nam.com` already resolve to an address the canonical host resolves to? A TXT
record would prove control of the *zone* — more than is needed, and a second thing to get wrong
in a support conversation that is already about DNS.

`Dns.GetHostAddressesAsync` from the BCL does it; there is no DNS client dependency. If a
deployment ever answers on addresses the canonical host does not share (split-horizon DNS,
per-tenant anycast), `DomainVerifier.CheckAsync` is the single seam to swap — callers only ever
see verified or not.

Verification is re-runnable, and it can take a domain *away*: a host that stops pointing at us
stops being verified, because staying verified would keep us minting links to an address that no
longer reaches the files. Renaming the host clears verification and issues a new token, or
renaming would be a way to serve an unverified address.

### What a verified host changes

1. **Links.** `WorkspaceDomainReader.DeliveryUrlAsync` is the one place a recipient link is
   built — the commit response, the recipient mail, the sender's own copy, the channel post, the
   approval release, the recurring cycle and the expiry reminder all go through it. A link that
   is branded in the confirmation mail and unbranded in the reminder is worse than no
   white-label at all.
2. **Isolation.** A verified host belongs to one workspace, and `Download.razor` refuses any
   share from a different one with a plain not-found. An agency's client seeing somebody else's
   file under the agency's own hostname is the worst failure this feature has; a redirect would
   confirm the share code exists, so it is a 404.

3. **Scope.** A tenant host serves recipient surfaces only — the delivery page, its stats page,
   the brand assets, and the API and static files those are built out of.
   `CustomDomainMiddleware` redirects everything else to the canonical address. Sign-in and the
   account area under an agency's hostname would be unbranded (deliberately) and an invitation
   to type our credentials somewhere we do not control.

Removing a domain is safe by construction: links already in an inbox keep working, because the
canonical host never stopped serving them.

### Not white-labelled

The signed-in app. A member of two workspaces would have no stable idea where they are — the
same reasoning `WorkspaceBrand` was written with. White-label covers what the *client* sees.

## Preview watermark (#155)

A tiled SVG behind a CSS `::after` on the preview container. The stored object is never touched,
no server-side re-encode happens, and the download is byte-identical to the upload — the
zero-knowledge story survives intact, and so does the range-request path.

The text is the workspace's own brand line if it has one, otherwise the sender's display name.
There is no free-text field: a watermark reading whatever the sender typed marks nobody in
particular, and the name is the thing that removes the anonymity of forwarding a screenshot. It
is XML-escaped and then percent-encoded into the data URI, so a name containing a quote or an
angle bracket cannot close the element.

**Refused, not dropped.** A free-plan commit asking for a watermark returns
`watermark_requires_paid_plan` rather than storing `false`. Silently disabling it would tell the
sender something untrue about who can screenshot their work — the one class of "graceful
degradation" that is a lie.

**It is defeatable.** An overlay always is: a determined client can request the preview URL
directly. What it removes is the *casual* screenshot-and-forward, which is the actual complaint.
Burning pixels into a transcoded preview stream is the upgrade path and costs a media pipeline.

## Not shipped

- Multiple domains per workspace, and apex domains that cannot CNAME (needs ALIAS/ANAME support
  at the edge).
- Certificate issuance. The deployment's edge terminates TLS for the custom host; that is an
  operations step, documented as such, not something the app performs.
- Per-tenant sender addresses in outbound mail — the mail still comes from us, and making it
  come from the agency means DKIM delegation, which is a second verification subsystem.
- Watermarked video/PDF *streams* (only the preview surface is overlaid), and a custom watermark
  text.
