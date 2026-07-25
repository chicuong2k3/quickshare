# Verify-Research Review — TeleCloud R2 Storage Integration

**Reviewer:** AI Verify agent  
**Date:** 2026-07-20  
**Spine:** `ARCHITECTURE-SPINE.md`  
**Verdict:** **FLAG**

---

## Critical Findings

### 1. [FACTUAL ERROR] Spine says ".NET 9.0" but project targets .NET 10

| Location | Spine Stack table, line 129 |
| --- | --- |
| Claim | `.NET — 9.0` |
| Reality | `src/QuickShare.Server/QuickShare.Server.csproj` line 3: `<TargetFramework>net10.0</TargetFramework>` |
| Impact | The spine references a runtime version the project does not use. All package binding, TFG resolution, and API surface checks will be against the wrong major version. |

**Fix:** Change Stack table entry to `10.0`.

---

### 2. [FACTUAL ERROR] AWSSDK.S3 v3 is in end-of-life; v4 is current

| Location | Spine Stack table, line 130 |
| --- | --- |
| Claim | `AWSSDK.S3 — latest stable` (implied v3.x, since that's the widely known package) |
| Reality | AWSSDK.S3 **v3.x entered maintenance mode 2026-03-01 and reaches end-of-support 2026-06-01** (NuGet gallery source). The current SDK is **AWSSDK.S3 4.x** (package ID unchanged, but major version bumped). The spine does not specify v4. |
| Impact | If implemented against v3, the project will immediately ship on a deprecated SDK. The v4 SDK may have API surface changes for the .NET 10 TFM. |
| Additional risk | The spine does not mention the **critical R2 compatibility flags**: `DisablePayloadSigning = true` AND `DisableDefaultChecksumValidation = true` are both required on `AmazonS3Config` / `PutObjectRequest` when targeting R2 (per Cloudflare R2 docs and verified via community reports — Stack Overflow `79690654`). Without these, all PUT operations fail with `SignatureDoesNotMatch`. The spine's AD-8 (cost-optimal) mentions `CompleteMultipartUploadAsync` and `HeadObjectAsync` but omits these mandatory configuration details. |

**Fix:** Specify `AWSSDK.S3 4.x` (not `latest stable`). Add a R2-SDK-gotchas companion doc covering `DisablePayloadSigning` and `DisableDefaultChecksumValidation`.

---

### 3. [TECHNICAL INACCURACY] AD-9 "SSE-S3" is the wrong label for R2's encryption model

| Location | Spine AD-9, lines 109-113 |
| --- | --- |
| Claim | `SSE-S3 encryption at rest` — "R2-managed AES-256-GCM encryption via SSE-S3" |
| Reality | R2 encrypts all objects at rest with **AES-256-GCM automatically, with no user configuration**. The S3 compatibility matrix (Cloudflare docs, 2026-06-08) shows the `x-amz-server-side-encryption` header and all SSE-S3/SSE-KMS headers as **❌ not implemented** for `PutObject`, `CreateMultipartUpload`, and `UploadPart`. R2 does NOT support the `x-amz-server-side-encryption: AES256` header that defines SSE-S3 in the AWS S3 API. Calling it "SSE-S3" implies developers should set `ServerSideEncryptionMethod` on the SDK, which will fail. The correct frame is: **always-on transparent encryption at rest** — it requires zero headers, zero config, and zero SDK-level encryption parameters. |
| Impact | A developer following the spine will try to configure SSE-S3 headers on the S3 client, get `403` errors, and waste debug time. |

**Fix:** Rewrite AD-9 to say "Always-on transparent AES-256-GCM encryption at rest (R2-native, no SSE headers needed or supported)." Remove the phrase "SSE-S3."

---

## Minor Findings

### 4. AWSSDK.S3 is not currently a project dependency

The spine's Stack table lists `AWSSDK.S3` as a named technology, but the `.csproj` has zero S3-related package references. This is fine for a new integration — but the spine should explicitly call out that this is a **new dependency to be added**, ideally noting the package name and recommended v4 version.

### 5. 15-minute upload presigned URL TTL — verified as reasonable

R2 supports presigned URLs from 1 second to 7 days (`604,800` seconds) per their docs (2026-04-24). 15 minutes for upload parts is well within limits and is a sensible default. **No issue here.**

### 6. 60-second download presigned URL TTL — verified as reasonable

Same source — well within limits. **No issue.**

### 7. Multipart threshold of 10 MB — verified as reasonable

R2 supports up to 10,000 parts and objects up to 5 TiB. 10 MB is a standard threshold. **No issue.**

---

## Summary

| # | Severity | Claim | Verdict |
| --- | --- | --- | --- |
| 1 | **HIGH** | .NET 9.0 | ❌ Stale — project is on .NET 10 |
| 2 | **HIGH** | AWSSDK.S3 latest stable | ❌ V3 is EOL; v4 required; missing mandatory R2 flags |
| 3 | **MEDIUM** | "SSE-S3" encryption label | ❌ R2 doesn't support SSE-S3 headers; encryption is transparent |
| 4 | **LOW** | Dependency status | Note: AWSSDK.S3 needs to be added |
| 5 | N/A | 15-min upload TTL | ✅ Verified correct |
| 6 | N/A | 60-sec download TTL | ✅ Verified correct |
| 7 | N/A | 10 MB multipart threshold | ✅ Verified correct |
