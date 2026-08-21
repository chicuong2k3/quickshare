# Object storage and workspace backup

Issues `#171` (S3-compatible storage backend), `#119` (data residency) and `#271` (scheduled
workspace backup). They answer the same two questions: where the bytes live, and how a customer
gets their data out without asking us.

## S3-compatible storage (`#171`)

### What actually changed

Nothing about how requests are made. `R2StorageProvider` always spoke S3 through the AWS SDK —
R2 *is* S3-compatible — and the single R2-specific line was deriving the endpoint from a
Cloudflare account id. The class is now `S3CompatibleStorageProvider` and the endpoint is
explicit config.

That one config value is the whole feature. Self-hosters were previously limited to local disk
for want of it, and MinIO, Garage, Ceph, Wasabi and AWS S3 itself now work with no code change.

### Configuration

The current section is `ObjectStore`:

```json
{
  "ObjectStore": {
    "Endpoint": "https://minio.internal:9000",
    "Region": "auto",
    "BucketName": "quickshare",
    "AccessKeyId": "…",
    "AccessKeySecret": "…",
    "ForcePathStyle": true,
    "PublicBaseUrl": ""
  }
}
```

For R2, omit `Endpoint` and set `AccountId` — the endpoint is derived.

**The legacy `R2` section still binds.** `ObjectStoreResolver.Resolve` prefers `ObjectStore`
when it is fully configured and falls back to `R2` otherwise. An options rename that silently
breaks every existing deployment on upgrade is not a feature, and the fallback is one function
so exactly one place decides which section wins.

`ForcePathStyle` defaults to true: R2, MinIO and Garage all want `host/bucket/key`, and
virtual-host style additionally needs wildcard DNS a self-hoster rarely has. AWS tolerates path
style, so the default is safe everywhere.

Without a configured store the app still starts on `LocalDiskStorageProvider`, and upload paths
throw `NotSupportedException` as before.

## Data residency (`#119`)

Pinning a delivery's bytes to a storage location — an EU bucket, a US bucket, an APAC bucket.

### Routing is derived from the object key

This is the load-bearing decision, and it is a trade rather than an obvious win.

The explicit alternative is a `region` argument on every `IStorageProvider` method and every call
site. That is more visible, but it means the region has to be threaded correctly through every
present *and future* path that touches an object, and a path that forgets it reads from the
wrong bucket and reports "not found" for a file that plainly exists.

Instead the region lives in the key, directly under `uploads/`:

```
uploads/eu/{userId}/{deliveryId}/{name}      pinned
uploads/{userId}/{deliveryId}/{name}         default bucket
```

`Delivery.StoragePath` already travels everywhere, so the bucket can always be found from the
one thing that is always present. `StorageRegions.FromKey` reads the segment back out, and
`S3CompatibleStorageProvider.Route` maps it to a client and bucket.

**Backward compatibility is the reason the scheme is safe rather than merely convenient.** Every
key written before residency existed has a user GUID where the region segment now sits, and a
32-character hex string can never match a region code — codes are `^[a-z0-9]{2,12}$`. Old
objects resolve to the default bucket exactly as before. That is asserted directly in the tests.

`InitR2MultipartUploadAsync` is the one method that takes a region parameter, because it is the
one call that *creates* the key. Everything after it agrees by construction.

Only `uploads/` is partitioned. Avatars and brand assets stay in the default bucket — scattering
a workspace logo across jurisdictions would be cost without meaning.

### Regions are a deployment capability

```json
"ObjectStore": {
  "Regions": [
    { "Code": "eu", "Label": "European Union (Frankfurt)", "BucketName": "qs-eu" },
    { "Code": "us", "Label": "United States", "BucketName": "qs-us", "Endpoint": "https://s3.us-east-1.amazonaws.com" }
  ]
}
```

Bucket-only covers R2, where buckets carry a location hint and share one endpoint. The endpoint
override covers AWS and multi-site MinIO, where a region genuinely is a different host — without
it this would not be residency for those deployments, just one datacentre with several folder
names. A region with no endpoint reuses the default client rather than opening a second
connection pool.

Half-configured regions are dropped, not rejected at startup: a typo in one region should not
take the whole store offline.

### Entitlement follows what the plans promise

| Plan | Behaviour |
|---|---|
| Studio | routes **per delivery** — an agency holds clients under different regimes |
| Pro | pins **one region** on the account; every upload follows it |
| Free / anonymous | default bucket |

Pro's per-request override is deliberately ignored rather than honoured, because honouring it
would quietly hand Pro the Studio feature.

An unknown or removed region code **degrades to the default bucket instead of failing**.
Residency is a deployment capability, and an operator who has not configured regions should see
uploads keep working rather than a validation error on a control the UI never showed.

### What it does not do

**Changing the setting does not move existing deliveries.** Relocating stored bytes across
jurisdictions is a copy, a verification and a delete on infrastructure we may not control;
doing it silently as a side effect of a settings change is the opposite of what a residency
control is for. The setting applies to the next upload, and the account page says so.

**Replacing a file keeps the delivery's original region.** Swapping an attachment must not
quietly move the bytes to another jurisdiction.

The recipient page shows a "Stored in {region}" badge, resolved from the delivery's recorded
region — and shows nothing when that region is no longer configured, because a label we cannot
resolve is worse than no badge.


## Scheduled workspace backup (`#271`)

The procurement question is "what happens if your region burns down?". The only answer that
survives a follow-up is a copy the customer holds themselves, so the destination is *their*
S3-compatible bucket with *their* credentials. A backup inside the same blast radius as the
thing it backs up is not a backup.

### Metadata, not bytes

The manifest carries the workspace, its seats, its folders, and one record per live delivery —
including the storage key, size and SHA-256.

It deliberately does **not** copy the objects. Shipping terabytes into someone's bucket on a
weekly schedule is a bandwidth bill they did not agree to, and the storage keys are what a
restore actually needs: with them, the customer pulls the objects on their own terms. This limit
is stated on the settings page, in both locales, rather than left to be discovered during an
actual disaster.

Manifest shape is versioned (`Version: 1`) so a future restore can branch on it.

### Running

Hourly Hangfire tick; each policy runs only once its own interval (daily or weekly) has elapsed,
so the tick frequency and the schedule are independent.

- **The manifest is uploaded with `ChecksumSHA256`**, so a restore can verify the copy without
  trusting our record of it.
- **Pruning happens only after a successful upload.** Deleting old copies on the strength of a
  run that produced nothing is how a retention policy deletes the last good backup.
- **A failed run records why and continues to the next policy.** One misconfigured destination
  must not stop everyone else's backup.
- **The destination client has a 30-second timeout and one retry.** A hanging endpoint must not
  stall the hourly sweep.
- Every run — success or failure — lands on the workspace audit timeline.

### Run now

`POST /api/workspace/backup/run` goes through **the same method** as the scheduled run. A "test
backup" that exercises a different code path proves nothing about the one that runs at 3am.

It is also the only way to find out whether the destination credentials work, because saving the
policy deliberately does not write a probe object: a settings save that silently creates objects
in someone else's bucket is a surprise, and this way the check is something the admin asks for
on purpose.

It returns **200 even when the run failed**, with the failure in the body. The request was fine;
the destination was not, and that distinction is exactly what the admin asked the endpoint to
tell them.

### Guards

HTTPS-only destination, for the same reason as the SIEM sink: credentials and a workspace
inventory crossing the network. Admin-only. One policy per workspace — several destinations is a
replication problem, and solving it here would mean owning partial-failure semantics across
them. The destination secret is write-only: the API reports whether one is set, never its value,
and a blank submit leaves the stored value alone.

**Restore is not automated.** The manifest is a documented, versioned JSON format and the
storage keys are in it; there is no import wizard. Building one that half-works would be worse
than a format an operator can read.

## Extending this

- **Adding a storage backend?** If it speaks S3, it needs no code — just config. If it does not,
  implement `IStorageProvider` and register it in the same block in `Program.cs`.
- **New code that builds an upload key?** Go through `R2ObjectKey.For` and pass the region.
  A hand-rolled key skips residency routing and lands the object in the default bucket.
- **New code that moves or copies an object?** Keep it inside its region unless the user has
  explicitly asked otherwise. Silent cross-jurisdiction movement is the failure this feature
  exists to prevent.
- **Changing the manifest?** Bump `ManifestVersion`. Anything reading an old manifest must keep
  working, because customers hold copies we cannot migrate.
- **Adding data to a workspace that a customer would need after a disaster?** Add it to
  `BuildManifestAsync`. A backup that silently omits the newest feature's data is the failure
  mode this subsystem exists to avoid.

## Tests

`tests/QuickShare.Server.Tests/E2E/StorageAndBackupTests.cs` (13) — endpoint derivation vs
explicit endpoint, `IsConfigured` completeness, legacy `R2` fallback and current-section
precedence; backup validation (plaintext endpoint, missing bucket, unknown frequency,
out-of-range retention), write-only secret, admin gating, the due-schedule arithmetic across
daily/weekly/disabled/half-configured, a failed run against an unreachable destination being
recorded rather than thrown, run-without-policy 404, and delete.

Round-tripping bytes against a live MinIO belongs in CI with a container, not in an in-memory
suite — what changed in `#171` is which endpoint the SDK is handed, and that is what these cases
pin.

`tests/QuickShare.Server.Tests/E2E/StorageResidencyTests.cs` (19) — key format with and without
a region, region codes that must be rejected, and the case the whole scheme rests on: a
pre-residency key with a user GUID in the region slot resolving to the default bucket.
Half-configured regions dropped; Studio routing per delivery, Pro pinned to one and ignoring a
per-request override, Free and anonymous unaffected; unknown regions degrading rather than
failing; labels resolved only for regions that still exist.
