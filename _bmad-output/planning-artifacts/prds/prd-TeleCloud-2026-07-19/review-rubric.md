# PRD Quality Review — TeleCloud

## Overall verdict

**Strong.** This PRD earns its pivot with evidence, names its risks honestly, and translates both the technical wedge (owned storage, E2E-before-upload) and the business model (bandwidth-aware pricing, VN payment rails) into verifiable FRs. The two unsolved problems — market existence and distribution — are correctly identified as non-technical and surfaced as Open Questions rather than glossed over. Minor mechanical issues around FR-ID ordering and a handful of placeholder numeric targets do not diminish its readiness.

## 1. Decision-readiness — strong

Decisions are stated as decisions, not buried. The two headline changes from the prior draft are called out prominently in §0. Trade-offs are explicit: "Telegram is both more expensive AND carries existential ToS/ban risk." The pivot off Telegram is backed by a cost table (addendum §B) and a ToS analysis (addendum §B.4) that distinguishes Bot API from MTProto terms — the kind of specificity that lets a reader judge the reasoning. Open Questions are genuinely open (market existence, distribution, password-key interaction) — none are rhetorical. `[NOTE FOR PM]` callouts are used sparingly and at real tensions (egress passthrough deferred, cold-overflow arch).

### Findings
- **none** — dimension is strong

## 2. Substance over theater — strong

No persona theater: four UJs with named protagonists (Huy, Mai, Trang, Cuong) each drive distinct product requirements. The Vision statement is product-specific (video editors, wedding photographers, 3D artists — not "creative professionals" in the abstract). No innovation theater — the wedge shift is explicitly evidence-forced. NFRs include specific bounds (VN network throughput, chunk-size benchmarking via OQ-6) rather than boilerplate. Counter-metrics named in §7.

### Findings
- **none** — dimension is strong

## 3. Strategic coherence — strong

Clear thesis: TeleCloud is a delivery tool for professionals whose IP safety, branded handoff, and payment needs are poorly served by WeTransfer/MASV/Fsend. Feature set follows from the thesis — not a grab bag. Success Metrics validate the thesis (paid conversion, gross margin, repeat delivery rate — not DAU/MAU). Counter-metrics guard against optimizing the wrong things (free volume, ad density, feature breadth). MVP scope is honest: it cuts egress passthrough and MoMo/ZaloPay because they need metering infra and proven demand.

### Findings
- **low** — §7/SM-4 (95% large-file success target) and SM-2 (80% margin) are noted as `[ASSUMPTION]` but lack a plan for when real data disproves them. *Fix:* add a note: "recalibrate against first month of production data; if off by >10pp, flag for re-evaluation."

## 4. Done-ness clarity — adequate

Most FRs include testable Consequences. FR-1's "files well beyond 2 GB succeed" is clear. FR-2's "chunk-level resume" is verifiable. FR-3's "whole-file SHA-256 verified after reassembly" is testable. However, a few consequences are underspecified:
- FR-9 (server-enforced quota): "Exceeding a limit returns 403/429 with an upgrade nudge" — what constitutes the "nudge" (message shape)?
- FR-15 (file management): "invalid sort → 400" is good; "non-empty folder delete → 409" is good — but folder CRUD scope is underspecified relative to the delivery management scope.

### Findings
- **medium** — FR-15's Consequences describe REST error codes but don't specify the complete folder model (nesting depth limit? folder rename? move delivery between folders?). *Fix:* add depth limit + move/rename operations, or defer to post-MVP.
- **medium** — Several FRs rely on `[ASSUMPTION]` numeric values (chunk size 256–512 MB, 3–5 trial deliveries, 80% margin) that downstream engineering cannot treat as spec until calibrated. *Fix:* add a note that these are pre-benchmark defaults and must be confirmed before implementation.

## 5. Scope honesty — strong

Non-Goals section (§5) is explicit and comprehensive (13 items). `[ASSUMPTION]` tags are used consistently across the PRD and indexed in §9. `[NOTE FOR PM]` at key deferrals (egress passthrough, cold-overflow). Open Questions density is appropriate for a pre-build PRD (7 items). De-scoping is honest: §6.2 lists exactly what's cut and why.

### Findings
- **none** — dimension is strong

## 6. Downstream usability — adequate

Glossary present and internally consistent. FR/UJ/SM IDs are unique and cross-references resolve. UJs each have a named protagonist. Each section stands alone with cross-references via section numbers and glossary terms. However:

### Findings
- **medium** — FR IDs in §4.4 are out of numerical order: FR-10 (expiry), FR-16 (download cap), FR-13 (paid delivery), FR-17 (egress passthrough). The gap between 10 and 16 plus regressions to 13 and 17 break the sequential expectation. *Fix:* renumber §4.4 FRs as FR-10/FR-11/FR-12/FR-13 and shift downstream FRs accordingly.

## 7. Shape fit — strong

Consumer product with meaningful UX → UJs with named protagonists ✓. Chain-top (feeds UX → architecture → stories) → glossary present, IDs used ✓. Brownfield → existing-code references (WTelegramClient, Blazor, FastEndpoints, archived Telegram code) accurate ✓. Not over-formalized for its stakes.

### Findings
- **none** — dimension is strong

## Mechanical notes

- **Glossary drift:** Minor — "Delivery page" (capitalized in glossary, lowercase variant in §4.8). Consistent otherwise.
- **ID continuity:** FR-10 → FR-16 → FR-13 → FR-17 discontinuity in §4.4 (flagged above).
- **Assumptions Index roundtrip:** Complete — all 12 inline `[ASSUMPTION]` tags appear in §9, and §9 entries all resolve to inline tags. No orphans ✓.
- **UJ protagonist naming:** All 4 UJs have named protagonists with inline context ✓.
- **Required sections:** All expected sections present for a chain-top launch PRD (Vision, Target User, Glossary, Features, Non-Goals, MVP, Success Metrics, Open Questions, Assumptions Index) ✓.
