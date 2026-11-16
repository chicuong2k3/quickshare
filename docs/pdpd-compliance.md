# PDPD compliance (#230)

What the product does about Vietnam's Decree 13/2023 on personal data protection, and — just as
importantly — what it does not claim to do.

**This is not legal advice and the wording has not been through a lawyer.** What is built is the
*mechanism*: versioned, recorded, withdrawable consent, and a documented breach path. The text
shown to people is in `SharedLoc.*.resx` under `pdpd_*` and is the thing to hand a reviewer.

## Consent

Decree 13 wants consent that is specific, informed and withdrawable, and it treats **transfer of
personal data abroad** as its own thing to consent to. Our storage is outside Vietnam, so the
consent text names both: processing, and transfer abroad.

- **Versioned.** `ConsentKinds.PdpdVersion` travels with every record. An agreement to last
  year's wording is not an agreement to this year's, and the status endpoint says so. Bump the
  version whenever the text changes; old rows keep naming what they saw.
- **One row per account, kind and version.** Re-granting an active consent is a no-op: a log with
  an entry per page load says nothing about when somebody actually agreed.
- **Withdrawal marks, never deletes.** *Consented on Tuesday, withdrew on Friday* is the record
  that matters; erasing the second half would erase the part that protects the person.
- **The row holds nothing new about anybody** — account id, kind, version, timestamps, hashed IP.
  A consent log that quietly becomes a second profile is its own privacy problem.

The banner is shown to signed-in accounts reading the site in Vietnamese, once per version. It is
not a modal and it does not block the product: consent that is a condition of using the thing you
already paid for is not freely given. Locale is what we have; it is not a claim about where
somebody lives, which is why withdrawal and re-granting live on the privacy page for everybody.

## Breach notification

Decree 13 requires notifying the Vietnamese authority (A08) **within 72 hours** of becoming aware
of a personal-data breach, and notifying affected people. That is a different clock and a
different recipient from GDPR's, so it is a separate branch in the incident runbook rather than a
footnote to the existing one — see `docs/ONCALL.md`.

## Cross-border transfer

Files and account data live in object storage outside Vietnam (see `docs/object-storage-and-backup.md`
and the residency work in #119). Data residency can pin *which* region a delivery lands in where
the deployment configures one, but the default is foreign, and the consent text says so plainly
rather than burying it.

## Not shipped

- A Vietnamese-hosted storage region. #119 makes one configurable; standing it up is an
  operations decision with a cost attached, not code.
- Automated A08 notification. The 72-hour clock is a human process with a form at the end of it;
  what is written down is the branch, the deadline and who owns it.
- Consent for anonymous senders. Nothing is stored about them beyond the delivery they created,
  and asking a stranger to agree to a data-processing notice before they can send a file would be
  consent theatre.
- A separate VN privacy page. The privacy page carries the section; two pages that must say the
  same thing eventually do not.
