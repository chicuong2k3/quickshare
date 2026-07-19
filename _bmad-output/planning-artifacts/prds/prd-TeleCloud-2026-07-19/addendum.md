# PRD Addendum — TeleCloud

> Economics, evidence, and rejected alternatives behind the PRD. Belongs to downstream architecture/UX, not the PRD narrative. Sources are the six party-mode research missions (2026-07-19) plus the original brainstorm (`_bmad-output/brainstorming/brainstorm-fileshare-differentiation-2026-07-18/`).

## A. The pivot in one line

From **"Telegram without the 2 GB ceiling"** (storage on Telegram, wedge = size) to **"workflow-grade delivery for creative pros"** (storage owned, wedge = encrypted + branded + tracked + locally-paid delivery). Both changes were forced by evidence, not preference.

## B. Storage backend decision — owned, not Telegram

### B.1 Cost scenario: 100 users × 500 GB/month, avg 1.5 downloads/link, 30-day expiry
- Total uploaded: 50 TB/mo. Egress served: 75 TB/mo. Peak concurrent on disk: ~50 TB.

| Backend | Server/mo | Storage | Egress (75 TB) | **Total/mo** |
|---|---|---|---|---|
| **Hetzner dedicated (2×22TB HDD, 1Gbps)** | ~$54 | in-disk | 55 TB over 20 TB free × €1/TB ≈ €55 | **~$115** |
| Hetzner + 48 TB disk | ~$80 | in-disk | +€55 | **~$145** |
| Telegram relay | ~$54 box | $0 | 75×2 = 150 TB → 130 over free × €1 ≈ €130 | **~$210** |
| Backblaze B2 | 0 | 50 TB × $6 = $300 | free-to-CDN | **~$300** |
| Cloudflare R2 | 0 | 50 TB × $15 = $750 | $0 | **~$750** |
| AWS S3 | 0 | $1,150 | 75 TB × $85 | **~$7,500** |

**Owned dedicated disk (~$115) is the cheapest and Telegram (~$210) is ~2× more.** Against ~$800–1,000 revenue (100 × $8–10) that's **~85–88% gross margin**.

### B.2 Why Telegram is 2× (the upload-push)
Ingress is free everywhere. The 2× is on the **upload side**: with Telegram, the server must *push* every uploaded byte up to Telegram (egress 1×), then serve it down on download (egress 1×) = 2× per single-download file. Local disk *writes* the upload to disk for free, then serves 1× on download = 1×. Telegram's penalty is worst on **single-download files** — the most common case for a send-file product.

### B.3 The core insight
Telegram's only economic edge appears when **hoarding** — data-at-rest exceeds your disk, so owned = buy disk, Telegram = free. But data-at-rest only grows if you keep files long / don't expire — i.e. hoard — which is **exactly the behavior that triggers Telegram bans**. A short-expiry delivery product does not hoard, so Telegram's edge ≈ 0 while its risk stays. **No economic reason to keep Telegram.** A "hybrid: overflow to Telegram when disk fills" idea is *backwards* — it pushes the largest, longest-lived (most ban-triggering) data onto the most ban-prone backend. Correct overflow is local disk → **Backblaze B2** ($6/TB, no ToS risk), never Telegram.

### B.4 Telegram ToS — what's actually true (verified from source)
- **Bot Platform Developer ToS §5.2(e)** bans building services that "diverge significantly from the intended use cases … (e.g., cloud storage sites)" and **§5.2(f)** bans circumventing rate limits / operating by proxy. **BUT these bind the Bot API**, which TeleCloud does not use.
- **API Terms** (`core.telegram.org/api/terms`) — the terms that bind the **MTProto userbot** path TeleCloud used (WTelegramClient) — contain **no explicit ban on file-hosting/storage**; only §1.4 "forbidden to interfere with the basic functionality" (elastic) and §1.5 (no training AI on Telegram data).
- So "ToS bans cloud storage" is **true for Bot API, not for the userbot path** — an over-statement in the first research pass, corrected here.
- The real risk is **behavioral, not textual**: TeleDrive (flagship Telegram-as-Drive) hosted service died, repo archived 2024-04; Teldrive's own README warns "You will be banned instantly if you misuse telegram API … Your files will be removed." Enforcement is discretionary, no SLA, no appeal. **Acceptable for a hobby tool; a bad bet under a paid product's implicit "your files are safe" promise.**

Sources: [Bot Developer ToS](https://telegram.org/tos/bot-developers) · [API ToS](https://core.telegram.org/api/terms) · [teldrive README](https://github.com/tgdrive/teldrive) · [teledrive archived](https://github.com/mgilangjanuar/teledrive).

## C. Bandwidth economics (research mission 5)
- **Cost to serve 1 TB ranges $0 → $90, ~90× spread.** Hetzner EU/OVH box ≈ $0 (within 20 TB included / unmetered; OVH unmetered), R2 $0 egress (storage $15/TB), Backblaze $0-to-CDN ($6/TB storage), Vultr $10/TB over 2 TB free, DigitalOcean $20/TB, CloudFront ~$85/TB, S3 ~$90/TB.
- **Contabo: 32 TB/mo on every plan incl. the $6.99 tier.** Hetzner dedicated: €1/TB over 20 TB, no hard cap.
- **"Bandwidth kills you" is true only on S3/CloudFront.** On commodity VPS/dedicated, bandwidth is a near-fixed cost.
- **MASV retail = $0.25/GB ($250/TB).** Wholesale cost $0–10/TB (Hetzner/R2/Backblaze) ⇒ **25–250× margin**; even CloudFront leaves ~3×.

Sources: [R2 pricing](https://developers.cloudflare.com/r2/pricing/) · [Backblaze B2](https://www.backblaze.com/cloud-storage/pricing) · [Hetzner traffic](https://docs.hetzner.com/robot/general/traffic/) · [OVH unmetered](https://us.ovhcloud.com/bare-metal/bandwidth/) · [Vultr bandwidth](https://blogs.vultr.com/Vultr-Announces-Reduced-Bandwidth-Pricing-2-Tb-Of-Free-Monthly-Egress-Free-Ingress-And-Global-Pooling) · [MASV pricing](https://massive.io/pricing/) · [AWS egress](https://egresscost.com/).

## D. Pricing model & tier matrix

**Principle: sell TRANSFER not RETENTION.** Cost scales with *downloads-per-link*, not file size. "Flat-fee services survive only if avg downloads/link stays low." Three solvency levers: **short expiry + download-count cap + egress passthrough at top tier.**

Who eats bandwidth risk, across incumbents (mission 6): only **MASV** passes egress through per-GB (sustainable). **WeTransfer Ultimate / TransferNow / Filemail / Smash** use per-transfer-size caps → *vendor* eats re-download overage (their loss exposure). **Send Anywhere** caps total downloads. **Short expiry** (WeTransfer 1–3 days, MASV 5-day) limits re-download windows.

**Proposed starting matrix (tunable config, not hardcoded):**

| | Free | Pro (~50–99k VND / ~$2–4) | Studio (~199–299k VND / ~$8–12) |
|---|---|---|---|
| Max file size | ~5 GB | ~50 GB | unlimited (chunked) |
| Monthly volume | small | moderate | large + **egress passthrough** over allowance |
| Max expiry | 3 days | 30 days | 30 days / optional no-expiry* |
| **Download-count cap/link** | ~5 | higher | high/configurable |
| Branded page | — | ✓ | ✓ |
| Download tracking | basic | ✓ | ✓ |
| Password | ✓ | ✓ | ✓ |
| Paid delivery (VietQR) | — | ✓ | ✓ |
| Concurrency | 1–2 | 3–4 | 6+ |

\* "no-expiry" is the retention cost-accumulation trap — bound it by a **max concurrent stored GB**, not "unlimited forever."

Incumbent reference (mission 1 & 6): WeTransfer €132M rev 2023 (+25%), 80M MAU, **600k+ paying** — the paying market is real. WeTransfer Starter $6.99 = 300 GB/mo; Ultimate ~$25 = 1 TB/transfer. Sources: [MASV](https://massive.io/pricing/) · [WeTransfer plan limits](https://wetransfer.com/help-center/subscriptions/plan-limits) · [Bending Spoons/WeTransfer](https://www.businesswire.com/news/home/20240731358717/en/Bending-Spoons-Acquires-WeTransfer).

## E. Who pays, and the IP-safety opening (mission 1)
- Highest-value payers = **video editors / post** (MASV lists Disney, Spotify, MrBeast). They pay for: no size cap, **auto-retry on failed transfer**, and **CSV billing to bill clients per delivery**. Not for raw size.
- **#1 complaint:** MASV per-download egress surprise ($275 bill for a 100 GB file re-downloaded). → validates our download-cap + passthrough transparency.
- **2025 WeTransfer ToS change** (files usable for AI training) drove a pro exodus; reversed, "but trust damage was done." → **client-side E2E is a real selling point to this segment**, reframing the brainstorm's belief that "big-file users don't care about privacy." They care about **IP**, which E2E protects.

## F. Vietnam market (mission 4) — localization is table-stakes, not a moat
- **Fsend.vn (by FPT)** already exists: VN UI, 10 GB/send, 7-day expiry, marketed to "dân làm phim ảnh." Same segment, same feature, backed by a telecom with brand + infra + payments. VN-localization alone is a **trap**.
- **Zalo** caps 1 GB PC / ~100 MB mobile — pros hit this, a real pain.
- **Stripe does NOT support Vietnam** (needs US LLC + EIN + US bank). Rails: **VietQR/bank transfer (frictionless for solo dev), MoMo, ZaloPay, VNPay.**
- Willingness-to-pay thin: 71% say they'd pay, 56% still pirate; "normal" sub anchor ~30k VND (~$1.20). SaaS market small but growing (~$210M 2025, 16–24% CAGR).
- **Defensible edge is a specific workflow** Fsend/Zalo/Drive do poorly: client-review/proofing, branded delivery, download tracking, longer expiry, VietQR pay — not "bigger/free-er."

Sources: [Fsend coverage](https://chungta.vn/cong-nghe/fsend-vn-khong-ranh-cong-nghe-van-gui-file-dung-luong-lon-de-dang-1106178.html) · [Zalo limits](https://fptshop.com.vn/tin-tuc/thu-thuat/cach-gui-video-dung-luong-lon-qua-zalo-va-cac-tips-can-biet-156347) · [Stripe VN unavailable](https://dodopayments.com/blogs/stripe-supported-countries-alternatives) · [VN piracy/WTP](https://e.vnexpress.net/news/tech/vietnam-innovation/vietnamese-turning-away-from-pirated-software-shared-accounts-5076552.html) · [Fshare pricing](https://www.fshare.vn/payment/service/package-vip).

## G. Free-rival landscape & the Firefox Send lesson (mission 2 & 3)
- "Free + unlimited + no-signup" is **saturated**: gigafile.nu **300 GB/file** (ad-funded, JP-only, self-hosted, 14 yrs, thin margins), Smash unlimited, bashupload 50 GB. Can't win on generosity.
- **How free-unlimited survives or dies:** ads (gigafile, only proven at scale), donations (Catbox — near-collapse, deplatformed from Patreon), freemium funnels; the durable path is a **real paid tier**.
- **Firefox Send died from abuse**: E2E + trusted URLs → ransomware/phishing haven, **no report-abuse button**; Mozilla killed it 2020. **Direct lesson: ship a report button + takedown day one** (FR-19). An operator-can't-read tool *must* moderate on metadata/identity/rate/report.
- **Bandwidth, not storage, is the killer** — gigafile self-hosts to survive; cloud egress bankrupts. Aligns with §B/§C.

Sources: [gigafile interview](https://dailyportalz.jp/kiji/interview-with-Gigafile) · [Firefox Send shutdown](https://vpnoverview.com/news/mozilla-suspends-firefox-send-due-to-concerns-over-malware-abuse/) · [Catbox blog](https://blog.catbox.moe/) · [Smash limits](https://fromsmash.com/help/articles/13885610-what-is-the-file-size-limit-on-smash).

## G2. Free-tier ad funding (research mission 7)

**Break-even math.** Hetzner bandwidth ~$0.001/GB. A Free user capped at ~2 GB file / 5 downloads / 3-day expiry costs ~$0.01 egress worst case. VN/SEA display RPM $0.10–$2 (among the world's lowest); popunder CPM ~$0.85–1 (Adsterra Asia). To cover $0.01: ~3–6 impressions at CPM $0.5–1, up to ~30 at CPM $0.10; multiply ~1.7× for 30–50% adblock among tech-savvy users. **Achievable but razor-thin — ads reduce the Free-tier loss, they do not make it profitable. Egress cap is the real lever, not CPM.**

**Placement (decided).** Ads only on the **recipient download page**, only for **Free-sender** deliveries. Cleanliness follows the **sender's Plan**: Pro/Studio → recipient clean (a sold feature — "ad-free handoff for your clients"); Free → recipient sees interstitial-countdown ads. New accounts get 3–5 clean trial deliveries. Rationale: the recipient of a Free delivery is often a *client of a would-be paying user*; dirty ads there poison the free→paid funnel, so paying senders and their clients must stay clean.

**Networks.** AdSense is out — its policies bar file-sharing sites, and E2E means no content control to satisfy them. Usable: **Adsterra / PopAds / Monetag(PropellerAds) / HilltopAds / AdMaven** (popunder, interstitial, in-page push; low payout thresholds). Risk: malvertising / user-hostile formats — mitigate with a curated allowlist + category blocks (gambling permitted, not egregious). Sources: [AdSense copyright policy](https://support.google.com/adspolicy/answer/6018015), [Adsterra alternatives](https://adsterra.com/blog/adsense-alternatives/), [PopAds review](https://affmaven.com/popads-review/).

**Anti-adblock.** **Ad-Shield** (ad-shield.io, Korean) = paid ad-recovery service: serves ads via your own subdomain to evade filter lists; claims >90% recovery / 4–5× adblock revenue. gigafile uses it (`ck.gigafile.nu`, flagged "Problematic/P1" by AdGuard — the arms race is live but outsourced). Chosen posture: **Ad-Shield-style recovery and/or soft-wall (ask + retry), NOT hard-block.** Hard-block loses the uBlock arms race and false-blocks Brave/Firefox-privacy/DNS-filter users from legitimate downloads; gigafile itself does not hard-block. Adblock rates: ~32% US, ~42% global, ~52% developers (cited, treat cautiously). Sources: [Ad-Shield](https://www.ad-shield.io/), [AdGuard flag](https://github.com/AdguardTeam/AdguardFilters/issues/220028), [adblock stats](https://backlinko.com/ad-blockers-users).

## H. Chunking mechanism (carried from brainstorm, backend-agnostic)
Chunk ciphertext (encrypt-before-chunk), each Chunk an independent AES-256-GCM AEAD unit (96-bit nonce, AAD = manifest_id‖index). Encrypted Manifest (name, mime, size, whole-file + per-chunk SHA-256, chunk list with storage refs, crypto params) — no key inside. Upload: encrypt → parallel chunk upload → hashes → commit (write manifest → Share Link = fragment key + Share ID). Download: split key + Share ID → fetch+decrypt manifest → parallel chunk download → verify each on arrival → reassemble → decrypt → verify whole-file hash. Resume per-chunk; bad chunk re-fetched alone; commit only when complete. Now targets **owned storage objects** instead of Telegram messages; takedown = delete objects by manifest, no decrypt.

## I. Rejected alternatives
- **Telegram as paid backend** — §B: more expensive than owned disk + existential ToS/ban risk. Rejected. Code archived at `codes/TeleCloud-telegram-archive/`.
- **Account rotation to dodge Telegram bans** — rejected in brainstorm (ban-wave linkage, legal exposure, dishonest). Moot now that Telegram is gone.
- **Hybrid overflow to Telegram** — backwards (pushes most-ban-triggering data to most-ban-prone backend). Correct overflow = Backblaze.
- **Stripe** — unavailable in VN.
- **"Biggest/free-est" wedge** — saturated (gigafile, Fsend). Rejected for workflow-delivery wedge.
- **Native mobile + LAN P2P fast-path** — real value but requires native app; deferred (web-only MVP).
- **Cloud object storage (R2/S3) as primary** — R2/Backblaze storage cost accumulates ($300–750/mo scenario) and S3 egress is fatal ($7.5k); owned disk wins. R2/Backblaze retained only as cold overflow.

## J. Foundational principles (unchanged)
- **No fallbacks / no stacked evasion.**
- **Proof not promises** — verifiable zero-knowledge, no oversell (Firefox Send / roasted-competitor lesson).
- **Defensibility is the business** (brand, user base, workflow fit, owned infra), not the code — closed server is fine for a solo dev who must charge.
- **The two unsolved problems are not technical:** (1) does the paying market exist as modeled (interview 5 real studios), (2) distribution against 80M-user incumbents. Economics are viable; these are not yet.
