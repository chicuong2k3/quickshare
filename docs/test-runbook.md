# Manual Test Runbook — QuickShare

Snapshot: **2026-08-16**. Ordered checklist for manually testing everything that is
**implemented and issues-closed** in the current `dev` checkout. Use with
[`docs/TESTABLE.md`](TESTABLE.md) (feature → automated coverage map) and
[`docs/backlog.md`](backlog.md) (tiered inventory).

This runbook answers: **what do I test first, and in what order?** Order is risk-first:
money path → core smoke → the latest security batch → ops/admin → long-tail.

## How to use

1. Run the automated suites first — they prove the basics before you click anything.
2. Work groups A→E in order. Each checkbox is `action → expected result`.
3. Record pass/fail + evidence (screenshot / response body / note). No evidence = not tested.
4. Items that need external config (SMTP, R2, PayOS, Turnstile) are marked **🔧 needs X**.
   Without that config the flow degrades safely (see TESTABLE.md §5) — mark them
   `SKIP (no config)` rather than fail.

```bash
# 0. automated baseline (~3 min)
dotnet test tests/QuickShare.Server.Tests    # expect 236 passed
dotnet test tests/QuickShare.Browser.Tests   # expect 30 passed

# run the app for manual work
dotnet run --project src/QuickShare          # → http://localhost:9000
```

---

## Group A — Money path (PayOS, manual test #7)

Highest priority: this is billing. Requires **🔧 PayOS sandbox keys + a public URL (ngrok)** for the webhook.

- [ ] Sign in as free user → `/pricing` → pick Pro monthly → `/upgrade` shows PayOS QR + amount matching `Pricing` config
- [ ] Scan QR with PayOS **sandbox** bank app → pay → webhook arrives → upgrade page shows success (immediately or after refresh)
- [ ] `User.Plan` = `pro`; start a ~6 GB upload → passes (Pro limit is 20 GB/file)
- [ ] `/account/billing` shows the new transaction
- [ ] Repeat for yearly + Studio (minimum: verify displayed amounts)
- [ ] Cancel mid-payment on the PayOS page → back in app → plan NOT activated, intent stays Pending/expires after 1 h
- [ ] Invalid-signature webhook → 401 (integration-covered; spot-check with a tampered body)

## Group B — Core sharing smoke (committed features)

Fast wins. Verify each works end-to-end in the browser.

### B1. Upload → share → download
- [ ] `/send` → drop a file → upload completes → share link appears
- [ ] Open the link in another browser → download → bytes match the original
- [ ] Encrypted upload (URL has `#` fragment key) → download decrypts in-browser (issue: E2E AES-256-GCM)
- [ ] Large-ish file (≥ multipart threshold, 10 MB) → progress shown, parts upload
- [ ] Drop network mid-upload → Resume UI appears → network back → upload completes (`UploadResume` flow)

### B2. Download page gates
- [ ] Delivery with OTP → wrong OTP → error; correct OTP → file phase
- [ ] Expired link → expired phase, no download
- [ ] Download-count limit reached → limit phase
- [ ] No-JS: disable JS → page still renders a usable download link (`DownloadNoScriptTests` path)
- [ ] Ad-reward interstitial for free sender plan → countdown → skip/download

### B3. Delivery extras
- [ ] `/account/deliveries` → manage a delivery (abort, delete)
- [ ] P2P share from a delivery → `/p2p/d/{code}` flow works (manual-only on real NAT/4G)

### B4. Quick Paste (#13)
- [ ] Paste an image on `/tools/online-clipboard` → get a link → link shows the image
- [ ] Paste text on `/tools/share-text` → link → recipient reads inline (copy button)

### B5. Monthly quota (#10)
- [ ] Upload past free quota → blocked with upgrade prompt (free = 15 GB/month)
- [ ] `/account/usage` shows the used bar

### B6. Scheduled send (#69, Pro)
- [ ] Set `DeliverAt` in the future → link inactive (scheduled phase)
- [ ] At the chosen time → link becomes active without touching it

### B7. Expiry countdown (#113)
- [ ] Open a delivery with expiry → live countdown ticks without refresh
- [ ] Under 1 h → "expires in X minutes" nudge state
- [ ] Hit 0 → expired phase swaps in

## Group C — Security & Trust batch (just shipped, issues #53 #82 #121 #148 #149 #175)

Each area has automated coverage; manual pass confirms the UX. Config-dependent items marked.

### C1. Report abuse / DMCA (#53)
- [ ] Open `/d/{code}` → "Report" link → dialog → pick category → submit → "Thanks" confirmation
- [ ] Anonymous (no account) report succeeds; invalid category rejected
- [ ] Admin (email in `Admin:Emails`) → `/admin/reports` → report listed → **Suspend** → confirm
- [ ] Suspended link: `/d/{code}` shows unavailable phase; `/api/downloads/{code}` → 410
- [ ] **Resolve** without action → report closed, link stays live
- [ ] `/abuse` policy page renders EN + VI (footer link)

### C2. Security headers + CSP (#82)
- [ ] Any page response has: `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`
- [ ] HTTPS (prod) also sends `Strict-Transport-Security` (local http won't)
- [ ] Upload → download full flow still works under CSP (connect-src allows R2 presigned `https:`)
- [ ] Account pages render directly and Unpoly fragment updates still work
- [ ] PWA/service-worker scripts still run (no CSP console block)

### C3. Noindex on delivery pages (#121)
- [ ] `/d/{code}` response header: `X-Robots-Tag: noindex, nofollow`
- [ ] `/d/{code}` HTML head contains `<meta name="robots" content="noindex, nofollow">`
- [ ] `/` and `/pricing` have **no** robots tag (marketing stays indexable)
- [ ] `/api/downloads/{code}` header also noindex

### C4. OTP lockout (#148)
- [ ] Create OTP-protected delivery → wrong OTP ×5 → 6th attempt: **423** + `Retry-After` header (~900 s)
- [ ] Correct OTP **also rejected** while locked
- [ ] Wait out the lock (or use a fresh delivery) → correct OTP succeeds and resets the counter
- [ ] Download endpoint (`/api/downloads/{code}?otp=…`) also locked at 423 while locked

### C5. New-device alert (#149) — 🔧 needs SMTP + Google OAuth
- [ ] Log in from a never-seen UA/IP → alert email "Đăng nhập từ thiết bị mới" arrives with device label + time
- [ ] Log in again from the same device → **no** second alert
- [ ] `/account` → "Known devices" card → lists devices with first/last seen
- [ ] Remove a device → next login from it triggers a new alert
- [ ] Email contains no raw IP (privacy) — device label only

### C6. Download CAPTCHA (#175) — 🔧 needs Turnstile site+secret keys + domain whitelist
- [ ] (Unconfigured default) wrong OTP ×N → no challenge, flow unchanged — **safe skip**
- [ ] With keys: wrong OTP ×3 → 4th verify returns 428 + `captcha_required` + `siteKey`
- [ ] Turnstile widget renders in the OTP phase → solve → verify proceeds automatically
- [ ] Normal users (no repeated failures) never see a puzzle

## Group D — Analytics, ops, trust (committed)

### D1. Analytics (#24)
- [ ] Do uploads + downloads → `/admin/analytics` (admin email) → funnel counts increase
- [ ] No PII visible (visitor cookie id + country code only)

### D2. Feature flags (#91)
- [ ] `/admin/flags` → toggle `previews` on → feature appears; off → gone
- [ ] Non-admin → 403

### D3. Legal pages (#93)
- [ ] Footer → `/terms`, `/privacy` render in EN and VI
- [ ] Cookie consent banner appears → accept → persists

### D4. Error alerting + health (#89)
- [ ] `/health` → 200 with db/email/storage checks
- [ ] Trigger a deliberate 500 → error appears in logs/Seq/Loki + alert fires

### D5. Backup/restore (#90)
- [ ] Follow `docs/backup-restore-runbook.md` → backup succeeds → restore into empty DB → data back

### D6. Orphan R2 sweep (#107) — 🔧 needs R2
- [ ] Abort an upload mid-flight → run sweep → orphaned objects reclaimed

### D7. CI/CD (#96)
- [ ] Push to `dev`/`main` → GitHub Actions: build + server tests + browser tests + deploy all green

## Group E — Long tail

### E1. Avatar on R2 public objects (#326 — manual test OPEN)
- [ ] `/account/profile` → upload avatar → image served from public R2 URL (not local disk)
- [ ] Avatar shows in navbar after refresh

### E2. Localization
- [ ] Language switch (EN↔VI) → all strings swap, `lang` attribute follows

### E3. Rate limiting (#52)
- [ ] Hammer `/api/*` from one IP → 429 + `Retry-After` header; authenticated users get a higher cap

---

## Open decisions

- **Files/Folders page (#8)** — manual test left blank; issue note says "under review —
  who uses it?" Decision: watch hit logs for `/files` + `/api/folders` for one week after
  next deploy; if zero, open an issue to remove it. **Until then: test or skip?**
- **PayPal/Stripe (#179)** — closed, PayOS-only is the decision. Nothing to test.

## Checklist for closing this runbook

- [ ] Group A (PayOS) fully exercised with evidence
- [ ] Groups B + C fully exercised (config-dependent items marked SKIP with reason)
- [ ] Group D items that have local infra (analytics, flags, legal, health) done
- [ ] `docs/TESTABLE.md` updated to match reality (e.g. security batch now committed, not working-tree)
- [ ] Every closed feature issue above has pass/fail recorded (in the issue or this file)
