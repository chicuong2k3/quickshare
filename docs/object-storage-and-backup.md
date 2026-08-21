# Object storage and workspace backup

Issues `#171` (S3-compatible storage backend) and `#271` (scheduled workspace backup). They
ship together because they answer the same two questions: where the bytes live, and how a
customer gets their data out without asking us.

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

### What is not here

**Data residency (`#119`) is not part of this.** Choosing an EU or APAC bucket per delivery
means the region has to travel with every object through `IStorageProvider` — roughly ten method
signatures and every call site — plus a `StorageRegion` column and a migration path for existing
objects. That is a separate change with its own risk, and doing it as a footnote to a config
generalization would be the wrong way to touch the download path.

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
